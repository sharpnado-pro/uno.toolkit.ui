﻿namespace System.Runtime.CompilerServices
{
#if !NET5 && !NET7_0_OR_GREATER
	/// <summary>
	/// Reserved to be used by the compiler for tracking metadata. This class should not be used by developers in source code.
	/// </summary>
	internal static class IsExternalInit
	{
	}
#endif
}
