using HuntOps.Application.Common;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Common;
using NodaTime;

namespace HuntOps.UnitTests.Application;

public sealed class InputValidationTests
{
    private static InputValidator NewValidator() => new(DateTimeZoneProviders.Tzdb);

    private static IReadOnlyDictionary<string, string[]> ErrorsOf(Action<InputValidator> act)
    {
        var v = NewValidator();
        act(v);
        var ex = Assert.Throws<RequestValidationException>(v.ThrowIfInvalid);
        return ex.Errors;
    }

    [Fact]
    public void Valid_iso_values_parse()
    {
        var v = NewValidator();

        Assert.Equal(new LocalDate(2027, 6, 12), v.Date("startDate", "2027-06-12", required: true));
        Assert.Equal(new LocalTime(17, 0), v.Time("endTime", "17:00"));
        Assert.Equal(new LocalTime(17, 0, 30), v.Time("endTime", "17:00:30"));
        Assert.Equal("America/Chicago", v.TimeZone("timeZoneId", " America/Chicago ", required: true));
        Assert.True(v.IsValid);
    }

    [Theory]
    [InlineData("2027-13-01")]
    [InlineData("06/12/2027")]
    [InlineData("June 12")]
    public void Bad_dates_report_the_field_and_expected_format(string value)
    {
        var errors = ErrorsOf(v => v.Date("startDate", value, required: true));

        Assert.Contains("yyyy-MM-dd", Assert.Single(errors["startDate"]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("5pm")]
    [InlineData("25:00")]
    public void Bad_times_report_the_field_and_expected_format(string value)
    {
        var errors = ErrorsOf(v => v.Time("endTime", value));

        Assert.Contains("HH:mm", Assert.Single(errors["endTime"]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://agency.example/file")]
    [InlineData("/relative/path")]
    [InlineData("file:///etc/passwd")]
    public void Only_absolute_http_urls_are_accepted(string url)
    {
        var errors = ErrorsOf(v => v.OptionalUrl("sourceUrl", url));

        Assert.True(errors.ContainsKey("sourceUrl"));
    }

    [Fact]
    public void Enum_values_are_case_insensitive_and_errors_list_allowed_values()
    {
        var v = NewValidator();
        Assert.Equal(ActionResolution.NotApplicable, v.Enum<ActionResolution>("status", "notapplicable", required: true));

        var errors = ErrorsOf(x => x.Enum<ActionResolution>("status", "done", required: true));
        var message = Assert.Single(errors["status"]);
        Assert.Contains("completed", message, StringComparison.Ordinal);
        Assert.Contains("reopened", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Numeric_enum_values_are_rejected()
    {
        var errors = ErrorsOf(v => v.Enum<ActionResolution>("status", "0", required: true));

        Assert.True(errors.ContainsKey("status"));
    }

    [Fact]
    public void Required_text_is_trimmed_and_length_checked()
    {
        var v = NewValidator();
        Assert.Equal("Kansas", v.RequiredText("name", "  Kansas  "));

        var errors = ErrorsOf(x =>
        {
            x.RequiredText("name", "   ");
            x.RequiredText("code", new string('X', 11), 10);
        });
        Assert.True(errors.ContainsKey("name"));
        Assert.True(errors.ContainsKey("code"));
    }

    [Fact]
    public void Error_messages_truncate_echoed_input()
    {
        var errors = ErrorsOf(v => v.TimeZone("timeZoneId", new string('z', 500), required: true));

        Assert.True(Assert.Single(errors["timeZoneId"]).Length < 150);
    }

    [Theory]
    [InlineData("Kansas  Dept. of  Wildlife", "kansas dept. of wildlife")]
    [InlineData("  WYOMING ", "wyoming")]
    public void Name_keys_ignore_case_and_whitespace(string input, string expected)
    {
        Assert.Equal(expected, TextKeys.NameKey(input));
    }

    [Theory]
    [InlineData("KS Resident Antelope – Firearm/Muzzleloader", "ks-resident-antelope-firearm-muzzleloader")]
    [InlineData("QC Élan Côte-Nord", "qc-elan-cote-nord")]
    [InlineData("!!!", "")]
    public void Slugs_are_ascii_kebab_case(string input, string expected)
    {
        Assert.Equal(expected, TextKeys.Slug(input));
    }
}
