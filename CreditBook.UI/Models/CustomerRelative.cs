namespace CreditBook.UI.Models;

public class CustomerRelative
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int RelativeId { get; set; }
    public Customer? Relative { get; set; }
}