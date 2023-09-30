namespace MenosRelato.Commands;

internal static class Conicet
{
    // Hardcoded map from javascript function cargarArea(e) in https://ri.conicet.gov.ar/subject/  ¯\_(ツ)_/¯ 
    public static int ToSubject(this string area) => area switch
    {
        string s when s.Contains("NATURALES") => 1,
        string s when s.Contains("TECNOLOGÍAS") => 53,
        string s when s.Contains("MÉDICAS") => 108,
        string s when s.Contains("AGRÍCOLAS") => 174,
        string s when s.Contains("SOCIALES") => 192,
        string s when s.Contains("HUMANIDADES") => 225,
        _ => throw new NotImplementedException("Area inesperada: " + area),
    };
}
