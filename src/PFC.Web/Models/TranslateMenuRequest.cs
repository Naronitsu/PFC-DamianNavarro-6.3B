namespace PFC.Web.Models;

public sealed record TranslateMenuRequest(
    string RestaurantId,
    string MenuId,
    string Text,
    string TargetLanguage);
