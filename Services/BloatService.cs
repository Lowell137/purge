using Kaldir.Models;

namespace Kaldir.Services;

// "Bloatware Remover": Windows ile gelen ya da üreticinin eklediği, çoğu kişinin kullanmadığı Mağaza uygulamaları.
// Liste bilerek dar ve güvenli tutuldu (Mağaza, Hesap Makinesi, Fotoğraflar gibi çekirdek uygulamalar yok); her uygulama
// Mağaza'dan yeniden kurulabilir.
public static class BloatService
{
    public static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.BingNews"] = "Microsoft News",
        ["Microsoft.BingWeather"] = "MSN Weather",
        ["Microsoft.BingSearch"] = "Bing Search",
        ["Microsoft.BingFinance"] = "Microsoft Money",
        ["Microsoft.BingSports"] = "Microsoft Sports",
        ["Microsoft.GetHelp"] = "Get Help",
        ["Microsoft.Getstarted"] = "Tips",
        ["Microsoft.WindowsFeedbackHub"] = "Feedback Hub",
        ["Microsoft.MicrosoftOfficeHub"] = "Microsoft 365 (Office hub)",
        ["Microsoft.MicrosoftSolitaireCollection"] = "Solitaire Collection",
        ["Microsoft.MixedReality.Portal"] = "Mixed Reality Portal",
        ["Microsoft.Microsoft3DViewer"] = "3D Viewer",
        ["Microsoft.Print3D"] = "Print 3D",
        ["Microsoft.OneConnect"] = "Mobile Plans",
        ["Microsoft.People"] = "People",
        ["Microsoft.SkypeApp"] = "Skype",
        ["Microsoft.Todos"] = "Microsoft To Do",
        ["Microsoft.PowerAutomateDesktop"] = "Power Automate",
        ["Microsoft.YourPhone"] = "Phone Link",
        ["Microsoft.ZuneMusic"] = "Media Player (Groove)",
        ["Microsoft.ZuneVideo"] = "Movies & TV",
        ["Microsoft.Xbox.TCUI"] = "Xbox TCUI",
        ["Microsoft.XboxApp"] = "Xbox Console Companion",
        ["Microsoft.XboxGameOverlay"] = "Xbox Game Overlay",
        ["Microsoft.XboxGamingOverlay"] = "Xbox Game Bar",
        ["Microsoft.XboxIdentityProvider"] = "Xbox Identity Provider",
        ["Microsoft.XboxSpeechToTextOverlay"] = "Xbox Speech To Text",
        ["Microsoft.GamingApp"] = "Xbox",
        ["Microsoft.549981C3F5F10"] = "Cortana",
        ["Microsoft.WindowsMaps"] = "Maps",
        ["Microsoft.Wallet"] = "Microsoft Pay",
        ["Microsoft.MicrosoftStickyNotes"] = "Sticky Notes",
        ["Microsoft.Office.OneNote"] = "OneNote",
        ["Microsoft.OutlookForWindows"] = "Outlook (new)",
        ["MicrosoftTeams"] = "Microsoft Teams (personal)",
        ["MSTeams"] = "Microsoft Teams",
        ["Clipchamp.Clipchamp"] = "Clipchamp",
        ["Disney.37853FC22B2CE"] = "Disney+",
        ["SpotifyAB.SpotifyMusic"] = "Spotify",
        ["4DF9E0F8.Netflix"] = "Netflix",
        ["BytedancePte.Ltd.TikTok"] = "TikTok",
        ["Facebook.Facebook"] = "Facebook",
        ["Facebook.Instagram"] = "Instagram",
        ["king.com.CandyCrushSaga"] = "Candy Crush Saga",
        ["king.com.CandyCrushSodaSaga"] = "Candy Crush Soda Saga",
        ["king.com.CandyCrushFriends"] = "Candy Crush Friends",
        ["flaregamesGmbH.RoyalRevolt2"] = "Royal Revolt 2",
        ["A278AB0D.MarchofEmpires"] = "March of Empires",
        ["Amazon.com.Amazon"] = "Amazon",
        ["LinkedIn.LinkedIn"] = "LinkedIn",
    };

    public static string PackageName(AppEntry e) => e.PackageFullName.Split('_')[0];

    public static List<(AppEntry app, string label)> Find(IEnumerable<AppEntry> all)
    {
        var res = new List<(AppEntry, string)>();
        foreach (var a in all.Where(x => x.IsStore))
            if (Known.TryGetValue(PackageName(a), out string? label)) res.Add((a, label));
        return res.OrderBy(r => r.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
