using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

namespace Aurora.Countries.HostilePackage;

/// <summary>
/// Records, in a process-wide slot, that this module's initialiser executed (ADR-0033 §5.6 D1).
/// </summary>
/// <remarks>
/// <para>
/// The runtime runs a module initialiser before the first method in the module executes, so this
/// record is the earliest observable sign that the package was given a thread. A refusal that
/// leaves it absent happened before execution; one that leaves it present did not.
/// </para>
/// <para>
/// The slot is keyed by this assembly's own file path, so every deployed copy is witnessed
/// separately and two tests loading two copies cannot see each other's record. It is
/// <see cref="AppDomain.SetData"/> rather than a static field because the tests must read it
/// without naming a type from this assembly — naming one would load the assembly into the test's
/// own context and run this initialiser there. The key's shape is a convention shared with
/// <c>PackageAdmissionFloorTests</c>; that class's positive control is what keeps the two in step.
/// </para>
/// </remarks>
internal static class ModuleInitialiserWitness
{
    private const string KeyPrefix = "Aurora.Countries.HostilePackage.ModuleInitialiserRan|";

    /// <summary>Runs before any other code in this module.</summary>
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "This fixture exists to carry a module initialiser: ADR-0033 §5.6 D1 asserts " +
            "a refusal happened before execution by observing that this initialiser did not run. " +
            "It is loaded only by the admission-floor tests, never as a library.")]
    internal static void RecordThatThisModuleRan()
    {
        string location = typeof(ModuleInitialiserWitness).Assembly.Location;
        string key = KeyPrefix + (location.Length == 0 ? "<no location>" : Path.GetFullPath(location));

        AppDomain.CurrentDomain.SetData(key, true);
    }
}
