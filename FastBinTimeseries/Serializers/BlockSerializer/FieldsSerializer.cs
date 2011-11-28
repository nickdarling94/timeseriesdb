using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using JetBrains.Annotations;
using NYurik.EmitExtensions;
using NYurik.FastBinTimeseries.CommonCode;

namespace NYurik.FastBinTimeseries.Serializers.BlockSerializer
{
    public class FieldsSerializer : BaseSerializer
    {
        private IList<MemberSerializerInfo> _memberSerializers;

        public FieldsSerializer([NotNull] Type valueType, string name = null)
            : base(valueType, name)
        {
            if (valueType.IsArray || valueType.IsPrimitive)
                throw new SerializerException("Unsupported type {0}", valueType);

            FieldInfo[] fis = valueType.GetFields(TypeExtensions.AllInstanceMembers);
            _memberSerializers = new List<MemberSerializerInfo>(fis.Length);

            foreach (FieldInfo fi in fis)
            {
                _memberSerializers.Add(new MemberSerializerInfo(fi, GetSerializer(fi.FieldType, fi.Name)));
            }
        }

        public MemberSerializerInfo this[string memberInfoName]
        {
            get { return MemberSerializers.FirstOrDefault(i => i.MemberInfo.Name == memberInfoName); }
        }

        public IList<MemberSerializerInfo> MemberSerializers
        {
            get { return _memberSerializers; }
            set
            {
                ThrowOnInitialized();
                _memberSerializers = value.ToList();
            }
        }

        public static BaseSerializer GetSerializer([NotNull] Type valueType, string name = null)
        {
            if (valueType.IsArray)
                throw new SerializerException("Arrays are not supported ({0})", valueType);

            if (valueType.IsPrimitive)
            {
                switch (Type.GetTypeCode(valueType))
                {
                    case TypeCode.SByte:
                    case TypeCode.Byte:
                        return new SimpleSerializer(valueType, name);

                    case TypeCode.Char:
                    case TypeCode.Int16:
                    case TypeCode.UInt16:
                    case TypeCode.Int32:
                    case TypeCode.UInt32:
                    case TypeCode.Int64:
                    case TypeCode.UInt64:
                    case TypeCode.Single:
                    case TypeCode.Double:
                    case TypeCode.Decimal:
                        return new MultipliedDeltaSerializer(valueType, name);

                    default:
                        throw new SerializerException("Unsupported primitive type {0}", valueType);
                }
            }

            if (valueType == typeof (UtcDateTime))
                return new UtcDateTimeSerializer(name);

            return new FieldsSerializer(valueType, name);
        }

        public override void Validate()
        {
            foreach (MemberSerializerInfo ms in _memberSerializers)
                ms.Validate();
            _memberSerializers = new ReadOnlyCollection<MemberSerializerInfo>(_memberSerializers);
            base.Validate();
        }

        protected override Expression GetSerializerExp(Expression valueExp, Expression codec,
                                                       List<ParameterExpression> stateVariables,
                                                       List<Expression> initBlock)
        {
            ThrowOnNotInitialized();

            // result = writeDelta1() && writeDelta2() && ...

            Expression result = null;
            foreach (MemberSerializerInfo member in _memberSerializers)
            {
                Expression t = member.Serializer.GetSerializer(
                    member.GetterFactory(valueExp), codec, stateVariables, initBlock);

                Expression exp = Expression.IsTrue(t);
                result = result == null ? exp : Expression.And(result, exp);
            }

            return result ?? Expression.Constant(true);
        }

        protected override void GetDeSerializerExp(Expression codec, List<ParameterExpression> stateVariables,
                                                   out Expression readInitValue, out Expression readNextValue)
        {
            ThrowOnNotInitialized();

            // T current;
            ParameterExpression currentVar = Expression.Variable(ValueType, "current");

            // (class)  T current = FormatterServices.GetUninitializedObject(typeof(T));
            // (struct) T current = default(T);
            BinaryExpression assignNewT = Expression.Assign(
                currentVar,
                ValueType.IsValueType
                    ? (Expression) Expression.Default(ValueType)
                    : Expression.Call(
                        typeof (FormatterServices), "GetUninitializedObject", null,
                        Expression.Constant(ValueType)));

            var readAllInit = new List<Expression> {assignNewT};
            var readAllNext = new List<Expression> {assignNewT};

            foreach (MemberSerializerInfo member in _memberSerializers)
            {
                Expression readInit, readNext;
                member.Serializer.GetDeSerializer(codec, stateVariables, out readInit, out readNext);
                
                Expression field = member.GetterFactory(currentVar);
                readAllInit.Add(Expression.Assign(field, readInit));
                readAllNext.Add(Expression.Assign(field, readNext));
            }

            readAllInit.Add(currentVar);
            readAllNext.Add(currentVar);

            readInitValue = Expression.Block(new[] {currentVar}, readAllInit);
            readNextValue = Expression.Block(new[] {currentVar}, readAllNext);
        }
    }
}