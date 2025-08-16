using System;
using System.Dynamic;

namespace a2c.Services.Impl
{
    // Adapter callable from both JavaScript (ClearScript) and C# dynamic code.
    // - Implements DynamicObject.TryInvoke to satisfy the DLR for `obj(...)` calls in C# scripts.
    // - Also provides Invoke overloads so engines that prefer method dispatch can use them.
    internal sealed class RequestExecuteAdapter : DynamicObject
    {
        private readonly Func<object?[]?, object?> _runner;

        public RequestExecuteAdapter(Func<object?[]?, object?> runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        // DLR dynamic invocation support (C# scripts): obj(...)
        public override bool TryInvoke(InvokeBinder binder, object?[]? args, out object? result)
        {
            result = _runner(args);
            return true;
        }

        // Convenience Invoke methods (some hosts may prefer member dispatch)
        public object? Invoke() => _runner(null);
        public object? Invoke(object? a) => _runner(new object?[] { a });
        public object? Invoke(object? a, object? b) => _runner(new object?[] { a, b });
        public object? Invoke(object? a, object? b, object? c) => _runner(new object?[] { a, b, c });
        public object? Invoke(object? a, object? b, object? c, object? d) => _runner(new object?[] { a, b, c, d });
        public object? Invoke(params object?[] args) => _runner(args);
    }
}
