// Polyfill so C# 9 `record` types compile on netstandard2.1.
//
// Records use `init`-only setters, which need this marker type. Newer .NET has it built in;
// netstandard2.1 (and Unity) don't, so we declare it ourselves. It's internal, so it never
// clashes with another assembly's copy. Unity's docs recommend exactly this.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
