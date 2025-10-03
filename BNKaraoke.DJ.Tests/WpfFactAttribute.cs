using System;
using Xunit;

namespace BNKaraoke.DJ.Tests
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class WpfFactAttribute : FactAttribute
    {
    }
}
