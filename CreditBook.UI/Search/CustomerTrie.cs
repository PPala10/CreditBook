using System.Collections.Generic;
using System.Linq;
using CreditBook.UI.Models;

namespace CreditBook.UI.Search;

public class CustomerTrie
{
    private class Node
    {
        public Dictionary<char, Node> Children { get; } = new();
        public List<Customer> CustomersHere { get; } = new();
    }

    private readonly Node _root = new();

    public void Insert(Customer customer)
    {
        var key = customer.Name.ToLowerInvariant();
        var node = _root;

        foreach (var ch in key)
        {
            if (!node.Children.TryGetValue(ch, out var child))
            {
                child = new Node();
                node.Children[ch] = child;
            }
            node = child;
        }

        node.CustomersHere.Add(customer);
    }

    public List<Customer> SearchByPrefix(string prefix)
    {
        var key = prefix.ToLowerInvariant();
        var node = _root;

        foreach (var ch in key)
        {
            if (!node.Children.TryGetValue(ch, out var child))
                return new List<Customer>();

            node = child;
        }

        var result = new List<Customer>();
        CollectAll(node, result);
        return result;
    }

    private static void CollectAll(Node node, List<Customer> result)
    {
        result.AddRange(node.CustomersHere);

        foreach (var child in node.Children.Values)
        {
            CollectAll(child, result);
        }
    }

    public void Rebuild(IEnumerable<Customer> customers)
    {
        _root.Children.Clear();
        _root.CustomersHere.Clear();

        foreach (var customer in customers)
        {
            Insert(customer);
        }
    }
}