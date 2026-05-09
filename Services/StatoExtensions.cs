using TrakingTool.Models;

namespace TrakingTool.Services;

public static class StatoExtensions
{
    public static string ToLabel(this Stato s) => s switch
    {
        Stato.Aperta => "Aperta",
        Stato.InCorso => "In corso",
        Stato.Sospesa => "Sospesa",
        Stato.Chiusa => "Chiusa",
        Stato.Rilasciata => "Rilasciata",
        Stato.Annullata => "Annullata",
        _ => s.ToString()
    };

    public static string ToCssClass(this Stato s) => s switch
    {
        Stato.Aperta => "tt-stato tt-stato-aperta",
        Stato.InCorso => "tt-stato tt-stato-incorso",
        Stato.Sospesa => "tt-stato tt-stato-sospesa",
        Stato.Chiusa => "tt-stato tt-stato-chiusa",
        Stato.Rilasciata => "tt-stato tt-stato-rilasciata",
        Stato.Annullata => "tt-stato tt-stato-annullata",
        _ => "tt-stato"
    };

    public static bool IsOpen(this Stato s) =>
        s is Stato.Aperta or Stato.InCorso or Stato.Sospesa;
}
