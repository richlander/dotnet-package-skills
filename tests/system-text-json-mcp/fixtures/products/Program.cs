using System.Text.Json;

var products = new List<Product>
{
    new("Widget", 9.99m, true),
    new("Gadget", 19.95m, false),
    new("Gizmo", 4.50m, true),
};

// TODO: Using System.Text.Json with default options (do NOT set a custom naming
// policy), serialize `products` to a file named "products.json" (indented), then read
// that file back, deserialize it into a List<Product>, and print one line per product
// in the exact form:
//   Name: <Name>, Price: <Price>, InStock: <InStock>
// Example: "Name: Widget, Price: 9.99, InStock: True"

public record Product(string Name, decimal Price, bool InStock);
