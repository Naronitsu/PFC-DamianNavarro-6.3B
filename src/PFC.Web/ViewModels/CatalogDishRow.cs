namespace PFC.Web.ViewModels;

public sealed record CatalogDishRow(
    string RestaurantId,
    string RestaurantName,
    string MenuId,
    string MenuTitle,
    string ItemName,
    double Price,
    string Currency,
    string RestaurantStatus,
    string TranslateLine,
    bool CanTranslate);
