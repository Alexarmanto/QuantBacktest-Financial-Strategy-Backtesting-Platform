using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace QuantBacktest.Web.Models;

/// <summary>
/// What POST /Data/Refresh hands to GET /Data across its redirect (post/redirect/get): the alert of P4-U12 and the
/// field errors of the form. The cookie TempData of the app only serializes simple types (a record would throw when
/// the cookie is written), so everything is stored as strings: one key per part of the alert, one key per field.
/// </summary>
internal static class DataTempData
{
    public const string AlertMessageKey = "Data.Alert.Message";
    public const string AlertHintKey = "Data.Alert.Hint";
    public const string AlertCommandKey = "Data.Alert.Command";
    public const string FieldErrorPrefix = "Data.Error.";

    public static void WriteAlert(ITempDataDictionary tempData, FormAlert alert)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        ArgumentNullException.ThrowIfNull(alert);
        tempData[AlertMessageKey] = alert.Message;
        if (alert.Hint is not null)
        {
            tempData[AlertHintKey] = alert.Hint;
        }

        if (alert.Command is not null)
        {
            tempData[AlertCommandKey] = alert.Command;
        }
    }

    /// <summary>The first error of each field, keyed by field name (symbol, tf).</summary>
    public static void WriteFieldErrors(ITempDataDictionary tempData, IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var error in errors.DistinctBy(e => e.Field, StringComparer.Ordinal))
        {
            tempData[FieldErrorPrefix + error.Field] = error.Message;
        }
    }

    /// <summary>Reads and consumes the alert; null when there is none.</summary>
    public static FormAlert? ReadAlert(ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        var hint = tempData[AlertHintKey] as string;
        var command = tempData[AlertCommandKey] as string;
        return tempData[AlertMessageKey] is string message ? new FormAlert(message, hint, command) : null;
    }

    /// <summary>Reads and consumes the field errors, keyed by field name.</summary>
    public static IReadOnlyDictionary<string, string> ReadFieldErrors(ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in tempData.Keys.Where(k => k.StartsWith(FieldErrorPrefix, StringComparison.Ordinal)).ToList())
        {
            if (tempData[key] is string message)
            {
                errors[key[FieldErrorPrefix.Length..]] = message;
            }
        }

        return errors;
    }
}
