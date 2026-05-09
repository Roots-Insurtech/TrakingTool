using System.Globalization;
using TrakingTool.Models;

namespace TrakingTool.Services;

public enum ParseMode
{
    /// <summary>Auto-rileva il formato e usa quello che produce più righe valide.</summary>
    Auto,

    /// <summary>Un campo per riga, blocchi separati da riga vuota (es. paste da OneNote outline).</summary>
    LinePerField,

    /// <summary>TSV con descrizione che può essere multiriga (rileva inizio record dalle date).</summary>
    SmartMultiline,

    /// <summary>TSV semplice: una riga di testo = una attività.</summary>
    OneLinePerRow
}

public sealed class ParsedRow
{
    public int LineNumber { get; init; }
    public Entry Entry { get; init; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
    public bool HasWarning => Warnings.Count > 0;
}

public sealed class ParseResult
{
    public ParseMode ModeUsed { get; init; }
    public List<ParsedRow> Rows { get; init; } = new();
}

public static class TsvParser
{
    private static readonly string[] DateFormats =
    {
        "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy",
        "d-M-yyyy", "dd-MM-yyyy"
    };

    public static ParseResult Parse(string? input, ParseMode mode, Stato defaultStato)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ParseResult { ModeUsed = mode == ParseMode.Auto ? ParseMode.LinePerField : mode };

        if (mode != ParseMode.Auto)
            return new ParseResult { ModeUsed = mode, Rows = ParseWith(input, mode, defaultStato) };

        // Auto: prova tutte e scegli quella con più righe valide (tiebreak: più righe totali)
        var candidates = new[] { ParseMode.LinePerField, ParseMode.SmartMultiline, ParseMode.OneLinePerRow };
        var best = candidates
            .Select(m => new ParseResult { ModeUsed = m, Rows = ParseWith(input, m, defaultStato) })
            .OrderByDescending(r => r.Rows.Count(p => p.IsValid))
            .ThenByDescending(r => r.Rows.Count)
            .First();
        return best;
    }

    private static List<ParsedRow> ParseWith(string input, ParseMode mode, Stato defaultStato) => mode switch
    {
        ParseMode.LinePerField => ParseLinePerField(input, defaultStato),
        ParseMode.SmartMultiline => ParseTsvSmart(input, defaultStato),
        ParseMode.OneLinePerRow => ParseTsvSimple(input, defaultStato),
        _ => new List<ParsedRow>()
    };

    private static List<ParsedRow> ParseTsvSimple(string input, Stato defaultStato)
    {
        var rows = new List<ParsedRow>();
        var lines = SplitLines(input);
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            rows.Add(ParseTsvLine(lines[i], i + 1, defaultStato));
        }
        return rows;
    }

    private static List<ParsedRow> ParseTsvSmart(string input, Stato defaultStato)
    {
        var rows = new List<ParsedRow>();
        var lines = SplitLines(input);
        ParsedRow? current = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var parts = line.Split('\t');

            if (parts.Length >= 4 && TryParseDate(parts[3].Trim(), out _))
            {
                if (current is not null) rows.Add(current);
                current = ParseTsvLine(line, i + 1, defaultStato);
            }
            else if (current is not null)
            {
                if (string.IsNullOrEmpty(line) && string.IsNullOrEmpty(current.Entry.Descrizione)) continue;
                current.Entry.Descrizione = string.IsNullOrEmpty(current.Entry.Descrizione)
                    ? line
                    : current.Entry.Descrizione + "\n" + line;
            }
        }
        if (current is not null) rows.Add(current);

        foreach (var r in rows)
            r.Entry.Descrizione = r.Entry.Descrizione.Trim();
        return rows;
    }

    private static ParsedRow ParseTsvLine(string line, int lineNumber, Stato defaultStato)
    {
        var row = new ParsedRow
        {
            LineNumber = lineNumber,
            Entry = new Entry { Stato = defaultStato }
        };

        var parts = line.Split('\t');
        if (parts.Length < 4)
        {
            row.Errors.Add($"Trovate {parts.Length} colonne (servono almeno 4: cliente, area, descrizione, data)");
            return row;
        }

        row.Entry.Cliente = parts[0].Trim();
        row.Entry.Area = parts[1].Trim();
        row.Entry.Descrizione = parts[2].Trim();

        if (TryParseDate(parts[3].Trim(), out var dataReg))
            row.Entry.DataRegistrazione = dataReg;
        else
            row.Errors.Add($"Data registrazione non valida: '{parts[3].Trim()}'");

        if (parts.Length >= 5)
        {
            var rilascioRaw = parts[4].Trim();
            if (!string.IsNullOrEmpty(rilascioRaw))
            {
                if (TryParseDate(rilascioRaw, out var dataRil))
                    row.Entry.DataRilascio = dataRil;
                else
                    row.Errors.Add($"Data rilascio non valida: '{rilascioRaw}'");
            }
        }

        ValidateNonEmpty(row);
        return row;
    }

    private static List<ParsedRow> ParseLinePerField(string input, Stato defaultStato)
    {
        var rows = new List<ParsedRow>();
        var lines = SplitLines(input);

        var blockLines = new List<string>();
        var blockStartLineNo = 1;

        void Flush()
        {
            if (blockLines.Count == 0) return;
            rows.Add(BuildRowFromBlock(blockLines, blockStartLineNo, defaultStato));
            blockLines = new List<string>();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
            }
            else
            {
                if (blockLines.Count == 0) blockStartLineNo = i + 1;
                blockLines.Add(line);
            }
        }
        Flush();

        return rows;
    }

    private static ParsedRow BuildRowFromBlock(List<string> lines, int blockStartLineNo, Stato defaultStato)
    {
        var row = new ParsedRow
        {
            LineNumber = blockStartLineNo,
            Entry = new Entry { Stato = defaultStato }
        };

        if (lines.Count < 4)
        {
            row.Errors.Add($"Blocco con {lines.Count} righe (servono almeno 4: cliente, area, descrizione, data)");
            return row;
        }

        row.Entry.Cliente = lines[0].Trim();
        row.Entry.Area = lines[1].Trim();

        // L'ultima riga deve essere una data; se anche la penultima lo è, allora ultima = rilascio, penultima = registrazione.
        var lastIsDate = TryParseDate(lines[^1].Trim(), out var lastDate);
        if (!lastIsDate)
        {
            row.Errors.Add($"Ultima riga del blocco non è una data valida: '{lines[^1].Trim()}'");
            return row;
        }

        int dateStartIdx;
        if (lines.Count >= 5 && TryParseDate(lines[^2].Trim(), out var prevDate))
        {
            row.Entry.DataRegistrazione = prevDate;
            row.Entry.DataRilascio = lastDate;
            dateStartIdx = lines.Count - 2;
        }
        else
        {
            row.Entry.DataRegistrazione = lastDate;
            dateStartIdx = lines.Count - 1;
        }

        var descrCount = dateStartIdx - 2;
        if (descrCount > 0)
            row.Entry.Descrizione = string.Join("\n", lines.GetRange(2, descrCount)).Trim();

        ValidateNonEmpty(row);
        return row;
    }

    private static void ValidateNonEmpty(ParsedRow row)
    {
        if (string.IsNullOrEmpty(row.Entry.Cliente)) row.Errors.Add("Cliente vuoto");
        if (string.IsNullOrEmpty(row.Entry.Area)) row.Errors.Add("Area vuota");
        if (string.IsNullOrEmpty(row.Entry.Descrizione)) row.Warnings.Add("Descrizione vuota");
    }

    private static string[] SplitLines(string input) =>
        input.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

    private static bool TryParseDate(string s, out DateOnly date) =>
        DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static string ToLabel(this ParseMode mode) => mode switch
    {
        ParseMode.Auto => "Auto",
        ParseMode.LinePerField => "Una riga per campo (blocchi separati da riga vuota)",
        ParseMode.SmartMultiline => "TSV intelligente (descrizione multiriga)",
        ParseMode.OneLinePerRow => "TSV semplice (una riga = una attività)",
        _ => mode.ToString()
    };
}
