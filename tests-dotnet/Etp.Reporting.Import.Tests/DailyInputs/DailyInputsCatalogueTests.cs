using System.Globalization;
using System.Reflection;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.DailyInputs;

namespace Etp.Reporting.Import.Tests.DailyInputs;

/// <summary>Every daily-inputs code has a value-free History template (spec 11.1).</summary>
public sealed class DailyInputsCatalogueTests
{
    private static string[] DeclaredCodes() => typeof(DailyInputsCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetValue(null)!).ToArray();

    [Fact]
    public void All_lists_every_declared_code_once()
    {
        var declared = DeclaredCodes();
        Assert.Equal(22, declared.Length);
        Assert.Equal(declared.Order(), DailyInputsCodes.All.Order());
        Assert.All(declared, code => Assert.StartsWith("DAILY_INPUTS_", code));
    }

    [Fact]
    public void Every_code_has_a_catalogue_template_with_no_value_in_it()
    {
        Assert.All(DeclaredCodes(), code =>
        {
            Assert.True(ImportDiagnosticCatalogue.IsKnown(code), code);
            var template = ImportDiagnosticCatalogue.Template(code);
            Assert.NotEqual(ImportDiagnosticCatalogue.GenericMessage, template);
            Assert.True(template.Length <= 500, code);
            Assert.DoesNotMatch(@"\d{4,}|[\\/:]{2}|\{", template);
            Assert.DoesNotContain("WLMHW", template);
            Assert.DoesNotContain("HEMW", template);
            Assert.Equal(template, ImportDiagnosticCatalogue.SafeMessage(code, template));
        });
    }

    [Fact]
    public void The_too_old_text_names_the_lookback_constant()
    {
        Assert.Contains($" {DailyInputsTemplate.LookbackDays.ToString(CultureInfo.InvariantCulture)} days",
            ImportDiagnosticCatalogue.Template(DailyInputsCodes.DateTooOld));
    }

    [Fact]
    public void The_finalised_day_text_is_the_designed_instruction()
    {
        Assert.Equal(
            "This business day is finalised, so its manual entries cannot change. Reopen the day, then put the file in the watch folder again.",
            ImportDiagnosticCatalogue.Template(DailyInputsCodes.DayFinalised));
    }
}
