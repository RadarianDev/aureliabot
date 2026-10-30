using Discord;

public static class ForcesCommands
{
    public static string OrderLine(PendingOrder order, GameConfig config)
    {
        var province = Display.Province(config, order.ProvinceId);
        var name = order.Name ?? "";
        return order.Kind switch
        {
            OrderKind.Raise => Texts.Get("forces.order.raise", ("name", name), ("province", province), ("cost", Display.Crowns(order.Cost))),
            OrderKind.Train => Texts.Get("forces.order.train", ("name", name), ("sides", TrainedSides(order, config)), ("cost", Display.Crowns(order.Cost))),
            OrderKind.Station => Texts.Get("forces.order.station", ("name", name), ("province", province)),
            _ => Texts.Get("forces.order.crackdown", ("province", province), ("cost", Display.Crowns(order.Cost))),
        };
    }

    private static int TrainedSides(PendingOrder order, GameConfig config)
    {
        if (order.UnitType is not { } type || order.Sides is not { } sides) return 0;
        var units = config.Constants.UnitsOf(type);
        return Math.Min(units.MaxSides, sides + units.SidesStep);
    }

    public static string PendingList(DatabaseService data, GameConfig config, Ministry ministry)
    {
        var orders = data.GetPendingOrders(ministry);
        return orders.Count == 0
            ? Texts.Get("forces.pending.none", ("ministry", ministry.DisplayName()))
            : Texts.Get("forces.pending.header", ("ministry", ministry.DisplayName())) + "\n"
              + string.Join("\n", orders.Select(o => Texts.Get("forces.pending.line", ("line", OrderLine(o, config)))));
    }

    private static string Paid(PendingOrder order, Ministry ministry, decimal balance) =>
        Texts.Get("forces.paid", ("cost", Display.Crowns(order.Cost)), ("ministry", ministry.DisplayName()), ("balance", Display.Crowns(balance)));

    private static string Reply(DatabaseService data, GameConfig config, Ministry ministry, params string[] lines) =>
        string.Join("\n", lines.Where(l => l.Length > 0)) + "\n\n" + PendingList(data, config, ministry);

    public static string Raise(DatabaseService data, GameConfig config, UnitType type, ulong userId, string name, string province)
    {
        if (ProvinceAutocomplete.Find(config, province) is not { } target) return Texts.Get("forces.refusal.noProvince", ("province", province));
        var result = data.OrderRaise(type, userId, name, target.Id, config);
        if (result is not { Order: { } order }) return result.Refusal ?? Texts.Get("forces.refusal.noUnit");
        var ministry = DatabaseService.MinistryOf(type);
        return Reply(data, config, ministry,
            Texts.Get("forces.raise.done", ("name", order.Name), ("province", target.Name),
                ("sides", config.Constants.UnitsOf(type).BaseSides), ("when", Display.NextResolution(data.GetGameState()))),
            Paid(order, ministry, result.Balance));
    }

    public static string Train(DatabaseService data, GameConfig config, UnitType type, ulong userId, string unitText)
    {
        if (UnitAutocomplete.Find(data, type, unitText) is not { } unit) return Texts.Get("forces.refusal.noUnit");
        var result = data.OrderTrain(type, userId, unit.Id, config);
        if (result is not { Order: { } order }) return result.Refusal ?? Texts.Get("forces.refusal.noUnit");
        var ministry = DatabaseService.MinistryOf(type);
        return Reply(data, config, ministry,
            Texts.Get("forces.train.done", ("name", unit.Name), ("from", unit.Sides), ("sides", TrainedSides(order, config)),
                ("when", Display.NextResolution(data.GetGameState()))),
            Paid(order, ministry, result.Balance));
    }

    public static string Station(DatabaseService data, GameConfig config, UnitType type, ulong userId, string unitText, string province)
    {
        if (UnitAutocomplete.Find(data, type, unitText) is not { } unit) return Texts.Get("forces.refusal.noUnit");
        if (ProvinceAutocomplete.Find(config, province) is not { } target) return Texts.Get("forces.refusal.noProvince", ("province", province));
        var result = data.OrderStation(type, userId, unit.Id, target.Id, config);
        if (result.Refusal is not null || result.Order is null) return result.Refusal ?? Texts.Get("forces.refusal.noUnit");
        var ministry = DatabaseService.MinistryOf(type);
        var when = Display.NextResolution(data.GetGameState());
        var line = result.Cancelled
            ? Texts.Get("forces.station.cancelled", ("name", unit.Name), ("province", target.Name))
            : result.Replaced
                ? Texts.Get("forces.station.replaced", ("name", unit.Name), ("province", target.Name), ("when", when))
                : Texts.Get("forces.station.done", ("name", unit.Name), ("province", target.Name), ("when", when));
        return Reply(data, config, ministry, line);
    }

    public static string Crackdown(DatabaseService data, GameConfig config, ulong userId, string province)
    {
        if (ProvinceAutocomplete.Find(config, province) is not { } target) return Texts.Get("forces.refusal.noProvince", ("province", province));
        var result = data.OrderCrackdown(userId, target.Id, config);
        if (result is not { Order: { } order }) return result.Refusal ?? Texts.Get("forces.refusal.noUnit");
        var c = config.Constants.Crackdown;
        return Reply(data, config, Ministry.Interior,
            Texts.Get("crackdown.done", ("province", target.Name), ("when", Display.NextResolution(data.GetGameState())),
                ("reduction", Display.Number(c.Reduction)), ("backlash", Display.Number(c.Backlash)), ("popularity", Display.Number(c.FailurePopularity))),
            Paid(order, Ministry.Interior, result.Balance));
    }

    public static async Task DisbandAsync(
        DatabaseService data, GameConfig config, Confirmations confirmations, UnitType type, ulong userId, string unitText,
        Func<string, MessageComponent?, Task> respond)
    {
        var ministry = DatabaseService.MinistryOf(type);
        if (data.ForcesRefusal(ministry, userId) is { } refusal)
        {
            await respond(refusal, null);
            return;
        }
        if (UnitAutocomplete.Find(data, type, unitText) is not { } unit)
        {
            await respond(Texts.Get("forces.refusal.noUnit"), null);
            return;
        }

        var pending = data.GetPendingOrders(ministry).Count(o => o.UnitId == unit.Id);
        var warning = Texts.Get("forces.disband.confirm", ("name", unit.Name), ("sides", unit.Sides), ("province", Display.Province(config, unit.ProvinceId)))
                      + (pending > 0 ? "\n" + Texts.Get("forces.disband.confirmOrders", ("count", pending)) : "");
        await respond(warning, confirmations.Create(userId, () => Task.FromResult(Disbanded(data, config, type, userId, unit.Id)), Texts.Get("forces.disband.button")));
    }

    private static string Disbanded(DatabaseService data, GameConfig config, UnitType type, ulong userId, long unitId)
    {
        var result = data.Disband(type, userId, unitId);
        if (result is not { Unit: { } unit }) return result.Refusal ?? Texts.Get("forces.refusal.noUnit");
        return Texts.Get("forces.disband.done", ("name", unit.Name), ("province", Display.Province(config, unit.ProvinceId)));
    }
}
