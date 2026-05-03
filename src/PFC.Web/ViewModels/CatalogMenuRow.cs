namespace PFC.Web.ViewModels;

public sealed record CatalogMenuRow(
    string RestaurantId,
    string RestaurantName,
    string MenuId,
    string MenuTitle,
    string OcrText,
    string RestaurantStatus);
