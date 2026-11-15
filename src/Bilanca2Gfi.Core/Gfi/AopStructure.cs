namespace Bilanca2Gfi.Core.Gfi;

public enum GfiSheet { Bilanca, Rdg, Dodatni }

/// <summary>
/// Structure of the GFI-POD form (version 6.x): which AOPs are sums, and how a konto's balance is signed.
/// The sum definitions mirror the formulas in the FINA template.
/// </summary>
public static class AopStructure
{
    public static GfiSheet SheetOf(int aop) => aop switch
    {
        <= 127 => GfiSheet.Bilanca,
        <= 229 => GfiSheet.Rdg,
        _ => GfiSheet.Dodatni,
    };

    /// <summary>
    /// Natural sign of a konto balance for an AOP: aktiva and rashodi are debit positions (D−P = +1),
    /// pasiva and prihodi are credit positions (P−D, i.e. −1 applied to D−P).
    /// </summary>
    public static int NaturalSign(int aop) => aop switch
    {
        <= 66 => +1,                       // aktiva incl. izvanbilančni zapisi (066)
        <= 127 => -1,                      // pasiva incl. izvanbilančni zapisi (127)
        <= 133 => -1,                      // poslovni prihodi
        <= 156 => +1,                      // poslovni rashodi
        <= 167 => -1,                      // financijski prihodi
        <= 175 => +1,                      // financijski rashodi
        176 or 177 => -1,                  // udio u dobiti
        178 or 179 => +1,                  // udio u gubitku
        185 => +1,                         // porez na dobit
        _ => +1,
    };

    /// <summary>Sum AOPs: expression of other AOPs. "a..b" is an inclusive range.</summary>
    public static readonly IReadOnlyDictionary<int, string> Sums = new Dictionary<int, string>
    {
        // Bilanca - aktiva
        [2] = "3+10+20+31+36",
        [3] = "4..9",
        [10] = "11..19",
        [20] = "21..30",
        [31] = "32..35",
        [37] = "38+46+53+63",
        [38] = "39..45",
        [46] = "47..52",
        [53] = "54..62",
        [65] = "1+2+37+64",
        // Bilanca - pasiva
        [67] = "68+69+70+76+77+84+87+90",
        [70] = "71+72-73+74+75",
        [77] = "78..83",
        [84] = "85-86",
        [87] = "88-89",
        [91] = "92..97",
        [98] = "99..109",
        [110] = "111..124",
        [126] = "67+91+98+110+125",
        // RDG
        [128] = "129..133",
        [134] = "135+136+140+144+145+146+149+156",
        [136] = "137..139",
        [140] = "141..143",
        [146] = "147+148",
        [149] = "150..155",
        [157] = "158..167",
        [168] = "169..175",
        [180] = "128+157+176+177",
        [181] = "134+168+178+179",
        [182] = "180-181",
        [186] = "182-185",
        // Dodatni
        [290] = "291..294",
    };

    /// <summary>AOPs that are the positive / negative part of another AOP (dobit/gubitak pairs).</summary>
    public static readonly IReadOnlyDictionary<int, (int Source, bool Positive)> Parts = new Dictionary<int, (int, bool)>
    {
        [183] = (182, true),
        [184] = (182, false),
        [187] = (186, true),
        [188] = (186, false),
    };

    public static bool IsComputed(int aop) => Sums.ContainsKey(aop) || Parts.ContainsKey(aop);

    /// <summary>Parses "3+10+20" / "4..9" / "71+72-73" into (aop, sign) terms.</summary>
    public static IEnumerable<(int Aop, int Sign)> Terms(string expression)
    {
        var sign = 1;
        var token = "";
        foreach (var ch in expression + "+")
        {
            if (ch is '+' or '-')
            {
                if (token.Length > 0)
                {
                    if (token.Contains(".."))
                    {
                        var parts = token.Split("..");
                        for (var a = int.Parse(parts[0]); a <= int.Parse(parts[1]); a++) yield return (a, sign);
                    }
                    else yield return (int.Parse(token), sign);
                }
                token = "";
                sign = ch == '+' ? 1 : -1;
            }
            else if (!char.IsWhiteSpace(ch)) token += ch;
        }
    }

    /// <summary>Balance-sheet pairs where a negative value moves to the "loss" AOP: zadržana dobit/preneseni gubitak, dobit/gubitak godine.</summary>
    public static readonly IReadOnlyDictionary<int, int> ProfitLossPairs = new Dictionary<int, int>
    {
        [85] = 86,
        [88] = 89,
    };
}
