﻿/**********************************************************\
|                                                          |
|                          hprose                          |
|                                                          |
| Official WebSite: http://www.hprose.com/                 |
|                   http://www.hprose.net/                 |
|                   http://www.hprose.org/                 |
|                                                          |
\**********************************************************/
/**********************************************************\
 *                                                        *
 * ObjectSerializer.cs                                    *
 *                                                        *
 * Object Serializer class for C#.                        *
 *                                                        *
 * LastModified: Apr 19, 2012                             *
 * Author: Ma Bingyao <andot@hprfc.com>                   *
 *                                                        *
\**********************************************************/
#if !(PocketPC || Smartphone || WindowsCE || dotNET10 || dotNET11 || WP70 || SL5)
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Hprose.Common;

namespace Hprose.IO {
    class ObjectSerializer {
        private delegate void SerializeDelegate(object value, HproseWriter writer);
        private static readonly Type typeofSerializeDelegate = typeof(SerializeDelegate);
        private static readonly Type typeofVoid = typeof(void);
        private static readonly Type typeofObject = typeof(object);
        private static readonly Type[] typeofArgs = new Type[] { typeofObject, typeof(HproseWriter) };
        private static readonly Type typeofException = typeof(Exception);
        private static readonly MethodInfo serializeMethod = typeof(HproseWriter).GetMethod("Serialize", new Type[] { typeofObject });
        private static readonly ConstructorInfo hproseExceptionCtor = typeof(HproseException).GetConstructor(new Type[] { typeof(string), typeofException });
        private SerializeDelegate serializeFieldsDelegate;
        private SerializeDelegate serializePropertiesDelegate;
        private SerializeDelegate serializeMembersDelegate;

#if (dotNET35 || dotNET4)
        private static readonly ReaderWriterLockSlim serializersCacheLock = new ReaderWriterLockSlim();
#else
        private static readonly ReaderWriterLock serializersCacheLock = new ReaderWriterLock();
#endif
        private static readonly Dictionary<Type, ObjectSerializer> serializersCache = new Dictionary<Type, ObjectSerializer>();

        private void InitSerializeFieldsDelegate(Type type) {
            Dictionary<string, MemberInfo> fields = HproseHelper.GetFields(type);
            DynamicMethod dynamicMethod = new DynamicMethod("$SerializeFields",
                typeofVoid,
                typeofArgs,
                type,
                true);
            ILGenerator gen = dynamicMethod.GetILGenerator();
            LocalBuilder value = gen.DeclareLocal(typeofObject);
            LocalBuilder e = gen.DeclareLocal(typeofException);
            foreach (KeyValuePair<string, MemberInfo> field in fields) {
                FieldInfo fieldInfo = (FieldInfo)field.Value;
                Label exTryCatch = gen.BeginExceptionBlock();
                if (type.IsValueType) {
                    gen.Emit(OpCodes.Ldarg_0);
                    gen.Emit(OpCodes.Unbox, type);
                }
                else {
                    gen.Emit(OpCodes.Ldarg_0);
                }
                gen.Emit(OpCodes.Ldfld, fieldInfo);
                if (fieldInfo.FieldType.IsValueType) {
                    gen.Emit(OpCodes.Box, fieldInfo.FieldType);
                }
                gen.Emit(OpCodes.Stloc_S, value);
                gen.Emit(OpCodes.Leave_S, exTryCatch);
                gen.BeginCatchBlock(typeofException);
                gen.Emit(OpCodes.Stloc_S, e);
                gen.Emit(OpCodes.Ldstr, "The field value can\'t be serialized.");
                gen.Emit(OpCodes.Ldloc_S, e);
                gen.Emit(OpCodes.Newobj, hproseExceptionCtor);
                gen.Emit(OpCodes.Throw);
                gen.Emit(OpCodes.Leave_S, exTryCatch);
                gen.EndExceptionBlock();
                gen.Emit(OpCodes.Ldarg_1);
                gen.Emit(OpCodes.Ldloc_S, value);
                gen.Emit(OpCodes.Call, serializeMethod);
            }
            gen.Emit(OpCodes.Ret);
            serializeFieldsDelegate = (SerializeDelegate)dynamicMethod.CreateDelegate(typeofSerializeDelegate);
        }

        private void InitSerializePropertiesDelegate(Type type) {
            Dictionary<string, MemberInfo> properties = HproseHelper.GetProperties(type);
            DynamicMethod dynamicMethod = new DynamicMethod("$SerializeProperties",
                typeofVoid,
                typeofArgs,
                type,
                true);
            ILGenerator gen = dynamicMethod.GetILGenerator();
            LocalBuilder value = gen.DeclareLocal(typeofObject);
            LocalBuilder e = gen.DeclareLocal(typeofException);
            foreach (KeyValuePair<string, MemberInfo> property in properties) {
                PropertyInfo propertyInfo = (PropertyInfo)property.Value;
                Label exTryCatch = gen.BeginExceptionBlock();
                if (type.IsValueType) {
                    gen.Emit(OpCodes.Ldarg_0);
                    gen.Emit(OpCodes.Unbox, type);
                }
                else {
                    gen.Emit(OpCodes.Ldarg_0);
                }
                MethodInfo getMethod = propertyInfo.GetGetMethod();
                if (getMethod.IsVirtual) {
                    gen.Emit(OpCodes.Callvirt, getMethod);
                }
                else {
                    gen.Emit(OpCodes.Call, getMethod);
                }
                if (propertyInfo.PropertyType.IsValueType) {
                    gen.Emit(OpCodes.Box, propertyInfo.PropertyType);
                }
                gen.Emit(OpCodes.Stloc_S, value);
                gen.Emit(OpCodes.Leave_S, exTryCatch);
                gen.BeginCatchBlock(typeofException);
                gen.Emit(OpCodes.Stloc_S, e);
                gen.Emit(OpCodes.Ldstr, "The property value can\'t be serialized.");
                gen.Emit(OpCodes.Ldloc_S, e);
                gen.Emit(OpCodes.Newobj, hproseExceptionCtor);
                gen.Emit(OpCodes.Throw);
                gen.Emit(OpCodes.Leave_S, exTryCatch);
                gen.EndExceptionBlock();
                gen.Emit(OpCodes.Ldarg_1);
                gen.Emit(OpCodes.Ldloc_S, value);
                gen.Emit(OpCodes.Call, serializeMethod);
            }
            gen.Emit(OpCodes.Ret);
            serializePropertiesDelegate = (SerializeDelegate)dynamicMethod.CreateDelegate(typeofSerializeDelegate);
        }

        private void InitSerializeMembersDelegate(Type type) {
            Dictionary<string, MemberInfo> members = HproseHelper.GetMembers(type);
            DynamicMethod dynamicMethod = new DynamicMethod("$SerializeFields",
                typeofVoid,
                typeofArgs,
                type,
                true);
            ILGenerator gen = dynamicMethod.GetILGenerator();
            LocalBuilder value = gen.DeclareLocal(typeofObject);
            LocalBuilder e = gen.DeclareLocal(typeofException);
            foreach (KeyValuePair<string, MemberInfo> member in members) {
                Label exTryCatch = gen.BeginExceptionBlock();
                if (type.IsValueType) {
                    gen.Emit(OpCodes.Ldarg_0);
                    gen.Emit(OpCodes.Unbox, type);
                }
                else {
                    gen.Emit(OpCodes.Ldarg_0);
                }
                if (member.Value is FieldInfo) {
                    FieldInfo fieldInfo = (FieldInfo)member.Value;
                    gen.Emit(OpCodes.Ldfld, fieldInfo);
                    if (fieldInfo.FieldType.IsValueType) {
                        gen.Emit(OpCodes.Box, fieldInfo.FieldType);
                    }
                }
                else {
                    PropertyInfo propertyInfo = (PropertyInfo)member.Value;
                    MethodInfo getMethod = propertyInfo.GetGetMethod();
                    if (getMethod.IsVirtual) {
                        gen.Emit(OpCodes.Callvirt, getMethod);
                    }
                    else {
                        gen.Emit(OpCodes.Call, getMethod);
                    }
                    if (propertyInfo.PropertyType.IsValueType) {
                        gen.Emit(OpCodes.Box, propertyInfo.PropertyType);
                    }
                }
                gen.Emit(OpCodes.Stloc_S, value);
                gen.Emit(OpCodes.Leave_S, exTryCatch);
                gen.BeginCatchBlock(typeofException);
                gen.Emit(OpCodes.Stloc_S, e);
                gen.Emit(OpCodes.Ldstr, "The member value can\'t be serialized.");
                gen.Emit(OpCodes.Ldloc_S, e);
                gen.Emit(OpCodes.Newobj, hproseExceptionCtor);
                gen.Emit(OpCodes.Throw);
                gen.Emit(OpCodes.Leave_S, exTryCatch);
                gen.EndExceptionBlock();
                gen.Emit(OpCodes.Ldarg_1);
                gen.Emit(OpCodes.Ldloc_S, value);
                gen.Emit(OpCodes.Call, serializeMethod);
            }
            gen.Emit(OpCodes.Ret);
            serializeMembersDelegate = (SerializeDelegate)dynamicMethod.CreateDelegate(typeofSerializeDelegate);
        }

        private ObjectSerializer(Type type) { 
            InitSerializeFieldsDelegate(type);
            InitSerializePropertiesDelegate(type);
            InitSerializeMembersDelegate(type);
        }

        public static ObjectSerializer Get(Type type) {
            ObjectSerializer serializer = null;
            try {
#if (dotNET35 || dotNET4)
                serializersCacheLock.EnterReadLock();
#else
                serializersCacheLock.AcquireReaderLock(-1);
#endif
                if (serializersCache.TryGetValue(type, out serializer)) {
                    return serializer;
                }
            }
            finally {
#if (dotNET35 || dotNET4)
                serializersCacheLock.ExitReadLock();
#else
                serializersCacheLock.ReleaseReaderLock();
#endif
            }
            try {
#if (dotNET35 || dotNET4)
                serializersCacheLock.EnterWriteLock();
#else
                serializersCacheLock.AcquireWriterLock(-1);
#endif
                if (serializersCache.TryGetValue(type, out serializer)) {
                    return serializer;
                }
                serializer = new ObjectSerializer(type);
                serializersCache[type] = serializer;
            }
            finally {
#if (dotNET35 || dotNET4)
                serializersCacheLock.ExitWriteLock();
#else
                serializersCacheLock.ReleaseWriterLock();
#endif
            }
            return serializer;
        }

        public void SerializeFields(object value, HproseWriter writer) {
            serializeFieldsDelegate(value, writer);
        }

        public void SerializeProperties(object value, HproseWriter writer) {
            serializePropertiesDelegate(value, writer);
        }

        public void SerializeMembers(object value, HproseWriter writer) {
            serializeMembersDelegate(value, writer);
        }
    }
}
#endif
