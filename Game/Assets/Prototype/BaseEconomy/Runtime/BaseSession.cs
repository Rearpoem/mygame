using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Prototype.BaseEconomy
{
    /// <summary>
    /// Deterministic, single-meal simulation shared by the UI and EditMode tests.
    /// No Unity types, wall clock, random source, public wages, or hidden cash sources.
    /// </summary>
    public sealed class BaseSession
    {
        private enum CookPhase { Unrecruited, ToSupplier, ToKitchen, Producing, ReadyToSell, Complete }

        private readonly BaseScenario _scenario;
        private readonly List<BaseLedgerEntry> _ledger = new List<BaseLedgerEntry>();
        private readonly ReadOnlyCollection<BaseLedgerEntry> _ledgerView;
        private int _tick;
        private int _treasuryCash;
        private int _supplierCash;
        private int _cookCash;
        private int _customerCash;
        private int _supplierGrain;
        private int _cookGrain;
        private int _cookMeals;
        private int _mealsConsumed;
        private int _firstMealTick;
        private CookPhase _cookPhase;
        private GridPosition _cookPosition;
        private GridPosition _customerPosition;
        private int _cookMoveProgress;
        private int _customerMoveProgress;
        private int _cookTravelTiles;
        private int _customerTravelTiles;
        private int _productionTicksRemaining;
        private bool _grainPurchased;
        private string _cookBlockedReason;
        private string _customerBlockedReason;

        public BaseSession(BaseScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException("scenario");
            scenario.Validate();
            _scenario = scenario.Clone();
            _ledgerView = _ledger.AsReadOnly();
            Reset();
        }

        public IReadOnlyList<BaseLedgerEntry> Ledger { get { return _ledgerView; } }

        /// <summary>Returns to the constructor's copied opening state, including tick and ledger.</summary>
        public void Reset()
        {
            _tick = 0;
            _treasuryCash = _scenario.initialTreasuryCash;
            _supplierCash = _scenario.initialSupplierCash;
            _cookCash = _scenario.initialCookCash;
            _customerCash = _scenario.initialCustomerCash;
            _supplierGrain = _scenario.initialSupplierGrain;
            _cookGrain = 0;
            _cookMeals = 0;
            _mealsConsumed = 0;
            _firstMealTick = -1;
            _cookPhase = CookPhase.Unrecruited;
            _cookPosition = _scenario.kitchenPosition;
            _customerPosition = _scenario.customerStart;
            _cookMoveProgress = 0;
            _customerMoveProgress = 0;
            _cookTravelTiles = 0;
            _customerTravelTiles = 0;
            _productionTicksRemaining = 0;
            _grainPurchased = false;
            _cookBlockedReason = "尚未招募";
            _customerBlockedReason = "等待厨师入驻";
            _ledger.Clear();
            Record(BaseLedgerEvent.InitialState, "开局", "", "", 0,
                "供粮者私有粮 " + _supplierGrain + "；厨师待招募；厨房为预置设施");
        }

        /// <summary>A recruitment transfer at the current tick. Repeated calls never transfer again.</summary>
        public BaseCommandResult RecruitCook()
        {
            if (_cookPhase != CookPhase.Unrecruited)
                return new BaseCommandResult(false, "厨师已经招募，本次未重复付款。");
            if (_treasuryCash < _scenario.recruitmentGrant)
                return new BaseCommandResult(false, "公库不足，未招募且未扣款。");

            _treasuryCash -= _scenario.recruitmentGrant;
            _cookCash += _scenario.recruitmentGrant;
            _cookPhase = CookPhase.ToSupplier;
            _cookBlockedReason = "";
            _customerBlockedReason = "";
            Record(BaseLedgerEvent.CookRecruited, "招募厨师", "公库", "厨师",
                _scenario.recruitmentGrant, "厨师入驻；招募款一次性成为个人启动资金；物品归属未变");
            return new BaseCommandResult(true, "招募成功；厨师自行取料，顾客自行前往厨房。");
        }

        /// <summary>One independent base tick. Cook acts before customer; terminal sessions do not advance.</summary>
        public BaseSnapshot Step()
        {
            if (_cookPhase == CookPhase.Unrecruited || _cookPhase == CookPhase.Complete)
                return GetSnapshot();
            _tick = checked(_tick + 1);
            UpdateCook();
            UpdateCustomer();
            return GetSnapshot();
        }

        public BaseSnapshot AdvanceTicks(int ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException("ticks");
            for (int i = 0; i < ticks; i++)
            {
                if (_cookPhase == CookPhase.Unrecruited || _cookPhase == CookPhase.Complete) break;
                Step();
            }
            return GetSnapshot();
        }

        /// <summary>Does not recruit automatically. A blocked run returns its reason after the given limit.</summary>
        public BaseSnapshot RunUntilFirstMeal(int maxTicks)
        {
            return AdvanceTicks(maxTicks);
        }

        public BaseSnapshot RunUntilFirstMeal()
        {
            return RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
        }

        public BaseSnapshot GetSnapshot()
        {
            bool recruited = _cookPhase != CookPhase.Unrecruited;
            bool complete = _cookPhase == CookPhase.Complete;
            GridPosition cookDestination = _cookPhase == CookPhase.ToSupplier ? _scenario.supplierStart : _scenario.kitchenPosition;
            bool cookWalking = _cookPhase == CookPhase.ToSupplier || _cookPhase == CookPhase.ToKitchen;
            int cookRemaining = cookWalking ? _cookPosition.ManhattanDistance(cookDestination) : 0;
            int customerRemaining = recruited && !complete ? _customerPosition.ManhattanDistance(_scenario.kitchenPosition) : 0;
            return new BaseSnapshot
            {
                Tick = _tick,
                IsCookRecruited = recruited,
                IsComplete = complete,
                FirstMealTick = _firstMealTick,
                TreasuryCash = _treasuryCash,
                SupplierCash = _supplierCash,
                CookCash = _cookCash,
                CustomerCash = _customerCash,
                TotalCash = (long)_treasuryCash + _supplierCash + _cookCash + _customerCash,
                SupplierGrain = _supplierGrain,
                CookGrain = _cookGrain,
                CookMeals = _cookMeals,
                MealsConsumed = _mealsConsumed,
                KitchenPosition = _scenario.kitchenPosition,
                SupplierPosition = _scenario.supplierStart,
                CookPosition = _cookPosition,
                CustomerPosition = _customerPosition,
                CookDestination = cookDestination,
                CustomerDestination = _scenario.kitchenPosition,
                SupplierActivity = _grainPurchased ? "本场供粮已完成" : "等待厨师现场取粮",
                SupplierBlockedReason = !_grainPurchased && _supplierGrain < _scenario.grainPerMeal ? "自有粮库存不足，本场未设补货" : "",
                CookActivity = CookActivity(),
                CookBlockedReason = _cookBlockedReason,
                CustomerActivity = CustomerActivity(),
                CustomerBlockedReason = _customerBlockedReason,
                CookTravelTiles = _cookTravelTiles,
                CustomerTravelTiles = _customerTravelTiles,
                CookRemainingTravelTiles = cookRemaining,
                CustomerRemainingTravelTiles = customerRemaining,
                CookMovementTicksRemaining = cookRemaining > 0 ? _scenario.movementTicksPerTile - _cookMoveProgress : 0,
                CustomerMovementTicksRemaining = customerRemaining > 0 ? _scenario.movementTicksPerTile - _customerMoveProgress : 0,
                ProductionTicksRemaining = _productionTicksRemaining,
                LedgerCount = _ledger.Count
            };
        }

        private void UpdateCook()
        {
            switch (_cookPhase)
            {
                case CookPhase.ToSupplier:
                    if (!MoveToward(ref _cookPosition, _scenario.supplierStart, ref _cookMoveProgress, ref _cookTravelTiles)) return;
                    TryPurchaseGrain();
                    break;
                case CookPhase.ToKitchen:
                    if (!MoveToward(ref _cookPosition, _scenario.kitchenPosition, ref _cookMoveProgress, ref _cookTravelTiles)) return;
                    _cookPhase = CookPhase.Producing;
                    _productionTicksRemaining = _scenario.productionTicks;
                    break;
                case CookPhase.Producing:
                    _productionTicksRemaining--;
                    if (_productionTicksRemaining > 0) return;
                    // The recipe's input is already privately owned; no wage or cash generation occurs.
                    _cookGrain -= _scenario.grainPerMeal;
                    _cookMeals++;
                    _cookPhase = CookPhase.ReadyToSell;
                    Record(BaseLedgerEvent.MealProduced, "厨师加工餐食", "", "", 0,
                        "厨师自有粮 -" + _scenario.grainPerMeal + "；厨师自有餐 +1；未发公共工资");
                    break;
            }
        }

        private void TryPurchaseGrain()
        {
            // Validate all preconditions before modifying any account or inventory.
            int price = _scenario.grainUnitPrice * _scenario.grainPerMeal;
            if (_supplierGrain < _scenario.grainPerMeal)
            {
                _cookBlockedReason = "供粮者库存不足，钱货均未交付";
                return;
            }
            if (_cookCash < price)
            {
                _cookBlockedReason = "厨师购粮资金不足，钱货均未交付";
                return;
            }
            _cookCash -= price;
            _supplierCash += price;
            _supplierGrain -= _scenario.grainPerMeal;
            _cookGrain += _scenario.grainPerMeal;
            _grainPurchased = true;
            _cookPhase = CookPhase.ToKitchen;
            _cookBlockedReason = "";
            Record(BaseLedgerEvent.GrainPurchased, "厨师现场购粮", "厨师", "供粮者", price,
                "粮 " + _scenario.grainPerMeal + "：供粮者 → 厨师；钱货同时交付");
        }

        private void UpdateCustomer()
        {
            if (!MoveToward(ref _customerPosition, _scenario.kitchenPosition, ref _customerMoveProgress, ref _customerTravelTiles))
            {
                _customerBlockedReason = "";
                return;
            }
            if (_cookMeals < 1)
            {
                _customerBlockedReason = "厨房尚无成餐，等待厨师取料或加工";
                return;
            }
            if (_customerCash < _scenario.mealUnitPrice)
            {
                _customerBlockedReason = "顾客购餐资金不足，钱货均未交付";
                return;
            }
            // Customer is physically at the kitchen; cash, ownership and consumption commit together.
            _customerCash -= _scenario.mealUnitPrice;
            _cookCash += _scenario.mealUnitPrice;
            _cookMeals--;
            _mealsConsumed++;
            _firstMealTick = _tick;
            _cookPhase = CookPhase.Complete;
            _customerBlockedReason = "";
            Record(BaseLedgerEvent.MealSoldAndConsumed, "顾客现场购餐并消费", "顾客", "厨师", _scenario.mealUnitPrice,
                "餐 1：厨师 → 顾客 → 消费；厨师自有餐 -1；已消费餐 +1");
        }

        private bool MoveToward(ref GridPosition position, GridPosition destination, ref int progress, ref int travelTiles)
        {
            if (position == destination) return true;
            progress++;
            if (progress < _scenario.movementTicksPerTile) return false;
            progress = 0;
            // Fixed x-before-y Manhattan path. Residents may share a tile; this scene has no obstacles.
            if (position.x != destination.x) position.x += Math.Sign(destination.x - position.x);
            else position.y += Math.Sign(destination.y - position.y);
            travelTiles++;
            return position == destination;
        }

        private string CookActivity()
        {
            switch (_cookPhase)
            {
                case CookPhase.Unrecruited: return "待招募";
                case CookPhase.ToSupplier: return _cookPosition == _scenario.supplierStart ? "等待现场购粮" : "自行前往供粮者";
                case CookPhase.ToKitchen: return "携自有粮返回厨房";
                case CookPhase.Producing: return "加工餐食";
                case CookPhase.ReadyToSell: return "持有成餐，等待顾客";
                default: return "首餐已售出";
            }
        }

        private string CustomerActivity()
        {
            if (_cookPhase == CookPhase.Unrecruited) return "等待厨师入驻";
            if (_cookPhase == CookPhase.Complete) return "已购买并消费首餐";
            if (_customerPosition != _scenario.kitchenPosition) return "自行前往厨房";
            return _cookMeals > 0 ? "等待现场购餐" : "到店等待餐食";
        }

        private void Record(BaseLedgerEvent kind, string name, string payer, string payee, int amount, string itemChanges)
        {
            _ledger.Add(new BaseLedgerEntry(_ledger.Count + 1, kind, name, payer, payee, amount, itemChanges, GetSnapshot()));
        }

        public string ExportLedgerCsv()
        {
            StringBuilder csv = new StringBuilder();
            csv.Append("sequence,base_tick,event,payer,payee,amount,item_changes,treasury_cash,supplier_cash,cook_cash,customer_cash,total_cash,supplier_grain,cook_grain,cook_meals,meals_consumed\n");
            foreach (BaseLedgerEntry entry in _ledger)
            {
                csv.Append(entry.Sequence).Append(',').Append(entry.Tick).Append(',')
                    .Append(CsvCell(entry.EventName)).Append(',').Append(CsvCell(entry.Payer)).Append(',')
                    .Append(CsvCell(entry.Payee)).Append(',').Append(entry.Amount).Append(',')
                    .Append(CsvCell(entry.ItemChanges)).Append(',').Append(entry.TreasuryCash).Append(',')
                    .Append(entry.SupplierCash).Append(',').Append(entry.CookCash).Append(',')
                    .Append(entry.CustomerCash).Append(',').Append(entry.TotalCash).Append(',')
                    .Append(entry.SupplierGrain).Append(',').Append(entry.CookGrain).Append(',')
                    .Append(entry.CookMeals).Append(',').Append(entry.MealsConsumed).Append('\n');
            }
            return csv.ToString();
        }

        public string ExportLedgerText()
        {
            StringBuilder text = new StringBuilder();
            text.Append("基地单餐测试账本（数值为临时测试值；时钟独立于战斗）\n");
            foreach (BaseLedgerEntry entry in _ledger)
            {
                text.Append('#').Append(entry.Sequence).Append(" | tick ").Append(entry.Tick).Append(" | ")
                    .Append(entry.EventName).Append(" | ")
                    .Append(entry.Payer.Length == 0 ? "无现金转账" : entry.Payer + " → " + entry.Payee)
                    .Append(" | 金额 ").Append(entry.Amount).Append('\n')
                    .Append("  ").Append(entry.ItemChanges).Append('\n')
                    .Append("  余额 公库/供粮者/厨师/顾客：").Append(entry.TreasuryCash).Append('/')
                    .Append(entry.SupplierCash).Append('/').Append(entry.CookCash).Append('/').Append(entry.CustomerCash)
                    .Append("；现金合计 ").Append(entry.TotalCash).Append('\n')
                    .Append("  归属 供粮者粮/厨师粮/厨师餐/已消费餐：").Append(entry.SupplierGrain).Append('/')
                    .Append(entry.CookGrain).Append('/').Append(entry.CookMeals).Append('/').Append(entry.MealsConsumed).Append('\n');
            }
            return text.ToString();
        }

        private static string CsvCell(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
