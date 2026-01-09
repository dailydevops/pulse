// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// Suppress CA1812 for test handlers and interceptors that are instantiated via DI
[assembly: SuppressMessage(
    "Major Code Smell",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Classes are instantiated via dependency injection in tests",
    Scope = "namespaceanddescendants",
    Target = "~N:NetEvolve.Pulse.Tests.Integration"
)]
