using System;
using System.Dynamic;
using System.Linq;
using System.Reflection;

namespace ParksComputing.Api2Cli.Scripting.Services.Impl {
    internal class CaseInsensitiveDynamicProxy : DynamicObject {
        private readonly object? _target;

        public CaseInsensitiveDynamicProxy(object? target) {
            _target = target;
        }

        private static bool IsLeaf(object? value) {
            if (value is null) {
                return true;
            }
            var t = value.GetType();
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || typeof(Delegate).IsAssignableFrom(t);
        }

        private static object? Wrap(object? value) {
            return IsLeaf(value) ? value : new CaseInsensitiveDynamicProxy(value);
        }

        public override bool TryGetMember(GetMemberBinder binder, out object? result) {
            result = null;
            if (_target is null) {
                return true;
            }

            var type = _target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

            var prop = type.GetProperty(binder.Name, flags);
            if (prop != null) {
                result = Wrap(prop.GetValue(_target));
                return true;
            }

            var field = type.GetField(binder.Name, flags);
            if (field != null) {
                result = Wrap(field.GetValue(_target));
                return true;
            }

            // Also allow parameterless method group access (e.g., treating a getter-like method as a member)
            var method = type.GetMethod(binder.Name, flags, null, Type.EmptyTypes, null);
            if (method != null) {
                result = Wrap(method.Invoke(_target, Array.Empty<object?>()));
                return true;
            }

            return false;
        }

        public override bool TrySetMember(SetMemberBinder binder, object? value) {
            if (_target is null) {
                return true;
            }
            var type = _target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
            var prop = type.GetProperty(binder.Name, flags);
            if (prop != null && prop.CanWrite) {
                prop.SetValue(_target, value);
                return true;
            }
            var field = type.GetField(binder.Name, flags);
            if (field != null) {
                field.SetValue(_target, value);
                return true;
            }
            return false;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result) {
            result = null;
            if (_target is null) {
                return true;
            }

            var type = _target.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
            var methods = type.GetMethods(flags)
                .Where(m => string.Equals(m.Name, binder.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Try arity match first
            var method = methods.FirstOrDefault(m => m.GetParameters().Length == (args?.Length ?? 0))
                         ?? methods.FirstOrDefault();

            if (method == null) {
                return false;
            }

            var returnValue = method.Invoke(_target, args ?? Array.Empty<object?>());
            result = Wrap(returnValue);
            return true;
        }
    }
}
