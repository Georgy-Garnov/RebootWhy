using System.Globalization;
using System.Runtime.CompilerServices;

namespace RebootWhy.Core.Tests;

/// <summary>Tests check English texts regardless of the language of the machine they run on.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseEnglish()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = english;
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;
    }
}
