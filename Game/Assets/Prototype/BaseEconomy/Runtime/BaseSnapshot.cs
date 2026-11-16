namespace Prototype.BaseEconomy
{
    public sealed class BaseCommandResult
    {
        public bool Success { get; private set; }
        public string Reason { get; private set; }

        internal BaseCommandResult(bool success, string reason)
        {
            Success = success;
            Reason = reason;
        }
    }

    /// <summary>A detached view. UI cannot mutate the state machine through a snapshot.</summary>
    public sealed class BaseSnapshot
    {
        public int Tick { get; internal set; }
        public bool IsCookRecruited { get; internal set; }
        public bool IsComplete { get; internal set; }
        public int FirstMealTick { get; internal set; }
        public int TreasuryCash { get; internal set; }
        public int SupplierCash { get; internal set; }
        public int CookCash { get; internal set; }
        public int CustomerCash { get; internal set; }
        public long TotalCash { get; internal set; }
        public int SupplierGrain { get; internal set; }
        public int CookGrain { get; internal set; }
        public int CookMeals { get; internal set; }
        public int MealsConsumed { get; internal set; }
        public GridPosition KitchenPosition { get; internal set; }
        public GridPosition SupplierPosition { get; internal set; }
        public GridPosition CookPosition { get; internal set; }
        public GridPosition CustomerPosition { get; internal set; }
        public GridPosition CookDestination { get; internal set; }
        public GridPosition CustomerDestination { get; internal set; }
        public string SupplierActivity { get; internal set; }
        public string SupplierBlockedReason { get; internal set; }
        public string CookActivity { get; internal set; }
        public string CookBlockedReason { get; internal set; }
        public string CustomerActivity { get; internal set; }
        public string CustomerBlockedReason { get; internal set; }
        public int CookTravelTiles { get; internal set; }
        public int CustomerTravelTiles { get; internal set; }
        public int CookRemainingTravelTiles { get; internal set; }
        public int CustomerRemainingTravelTiles { get; internal set; }
        public int CookMovementTicksRemaining { get; internal set; }
        public int CustomerMovementTicksRemaining { get; internal set; }
        public int ProductionTicksRemaining { get; internal set; }
        public int LedgerCount { get; internal set; }
    }

    public enum BaseLedgerEvent
    {
        InitialState,
        CookRecruited,
        GrainPurchased,
        MealProduced,
        MealSoldAndConsumed
    }

    /// <summary>Immutable post-event balances and ownership, including zero-cash production events.</summary>
    public sealed class BaseLedgerEntry
    {
        public int Sequence { get; private set; }
        public int Tick { get; private set; }
        public BaseLedgerEvent EventKind { get; private set; }
        public string EventName { get; private set; }
        public string Payer { get; private set; }
        public string Payee { get; private set; }
        public int Amount { get; private set; }
        public string ItemChanges { get; private set; }
        public int TreasuryCash { get; private set; }
        public int SupplierCash { get; private set; }
        public int CookCash { get; private set; }
        public int CustomerCash { get; private set; }
        public long TotalCash { get; private set; }
        public int SupplierGrain { get; private set; }
        public int CookGrain { get; private set; }
        public int CookMeals { get; private set; }
        public int MealsConsumed { get; private set; }

        internal BaseLedgerEntry(int sequence, BaseLedgerEvent eventKind, string eventName,
            string payer, string payee, int amount, string itemChanges, BaseSnapshot after)
        {
            Sequence = sequence;
            Tick = after.Tick;
            EventKind = eventKind;
            EventName = eventName;
            Payer = payer;
            Payee = payee;
            Amount = amount;
            ItemChanges = itemChanges;
            TreasuryCash = after.TreasuryCash;
            SupplierCash = after.SupplierCash;
            CookCash = after.CookCash;
            CustomerCash = after.CustomerCash;
            TotalCash = after.TotalCash;
            SupplierGrain = after.SupplierGrain;
            CookGrain = after.CookGrain;
            CookMeals = after.CookMeals;
            MealsConsumed = after.MealsConsumed;
        }
    }
}
