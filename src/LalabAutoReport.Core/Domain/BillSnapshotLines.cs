namespace LalabAutoReport.Core.Domain;

public static class BillSnapshotLines
{
    public static IEnumerable<CustomerBillLine> Included(CustomerBill bill)
    {
        var orders = bill.Orders ?? new List<CustomerBillOrder>();
        var includedOrders = orders.Where(o => o.IsIncluded).Select(o => o.OrderId).ToHashSet();
        return (bill.Lines ?? new List<CustomerBillLine>()).Where(l => l.IsIncluded &&
            (orders.Count == 0 || includedOrders.Contains(l.OrderId)));
    }
}
