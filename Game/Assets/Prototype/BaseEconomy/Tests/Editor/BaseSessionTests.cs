using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Prototype.BaseEconomy.Tests
{
    public sealed class BaseSessionTests
    {
        private BaseScenario _scenario;

        [SetUp]
        public void ReadTheSameJsonThatThePlayableUses()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Prototype", "BaseEconomy", "default.json");
            Assert.That(File.Exists(path), Is.True, "Missing shared first-meal test configuration: " + path);
            _scenario = JsonUtility.FromJson<BaseScenario>(File.ReadAllText(path));
            Assert.That(_scenario, Is.Not.Null);
            _scenario.Validate();
        }

        [Test]
        public void FirstMealMatchesEveryHandCalculatedBalanceAndOwnership()
        {
            BaseSession session = new BaseSession(_scenario);
            AssertBalances(session.GetSnapshot(), 100, 10, 0, 20);
            AssertOwnership(session.GetSnapshot(), 1, 0, 0, 0);
            Assert.That(session.GetSnapshot().IsCookRecruited, Is.False);

            Assert.That(session.RecruitCook().Success, Is.True);
            AssertBalances(session.GetSnapshot(), 88, 10, 12, 20);
            AssertOwnership(session.GetSnapshot(), 1, 0, 0, 0);
            Assert.That(session.GetSnapshot().Tick, Is.Zero, "Recruitment is an explicit transfer at tick 0.");

            session.AdvanceTicks(2);
            AssertBalances(session.GetSnapshot(), 88, 16, 6, 20);
            AssertOwnership(session.GetSnapshot(), 0, 1, 0, 0);
            Assert.That(session.GetSnapshot().CookPosition, Is.EqualTo(_scenario.supplierStart));

            BaseSnapshot done = session.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            Assert.That(done.IsComplete, Is.True);
            Assert.That(done.FirstMealTick, Is.EqualTo(7));
            AssertBalances(done, 88, 16, 16, 10);
            AssertOwnership(done, 0, 0, 0, 1);
            Assert.That(session.Ledger.Count, Is.EqualTo(5));
            BaseLedgerEntry production = session.Ledger[3];
            Assert.That(production.EventKind, Is.EqualTo(BaseLedgerEvent.MealProduced));
            Assert.That(production.Amount, Is.Zero, "Private production does not receive a public wage.");
            Assert.That(production.CookGrain, Is.Zero);
            Assert.That(production.CookMeals, Is.EqualTo(1));
            Assert.That(production.MealsConsumed, Is.Zero, "Ledger preserves ownership before same-tick sale.");
            Assert.That(session.Ledger[4].EventKind, Is.EqualTo(BaseLedgerEvent.MealSoldAndConsumed));
            Assert.That(session.Ledger[4].Payer, Is.EqualTo("顾客"));
            Assert.That(session.Ledger[4].Payee, Is.EqualTo("厨师"));
        }

        [Test]
        public void EveryTickAndLedgerEntryConservesCashAndTracksTheOnlyIngredient()
        {
            BaseSession session = new BaseSession(_scenario);
            long initialCash = session.GetSnapshot().TotalCash;
            session.RecruitCook();
            while (!session.GetSnapshot().IsComplete && session.GetSnapshot().Tick < _scenario.runUntilFirstMealMaxTicks)
            {
                BaseSnapshot state = session.Step();
                Assert.That(state.TotalCash, Is.EqualTo(initialCash));
                int ingredientEquivalent = state.SupplierGrain + state.CookGrain +
                    _scenario.grainPerMeal * (state.CookMeals + state.MealsConsumed);
                Assert.That(ingredientEquivalent, Is.EqualTo(_scenario.initialSupplierGrain));
            }
            Assert.That(session.GetSnapshot().IsComplete, Is.True);
            for (int i = 0; i < session.Ledger.Count; i++)
            {
                Assert.That(session.Ledger[i].Sequence, Is.EqualTo(i + 1));
                Assert.That(session.Ledger[i].TotalCash, Is.EqualTo(130));
                Assert.That(session.Ledger[i].TotalCash, Is.EqualTo((long)session.Ledger[i].TreasuryCash +
                    session.Ledger[i].SupplierCash + session.Ledger[i].CookCash + session.Ledger[i].CustomerCash));
            }
        }

        [Test]
        public void DuplicateRecruitmentAndTerminalAdvanceNeverPostAgain()
        {
            BaseSession session = new BaseSession(_scenario);
            session.RecruitCook();
            string recruitedLedger = session.ExportLedgerCsv();
            Assert.That(session.RecruitCook().Success, Is.False);
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(recruitedLedger));
            session.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            string terminalLedger = session.ExportLedgerCsv();
            BaseSnapshot terminal = session.GetSnapshot();
            session.Step();
            session.AdvanceTicks(100);
            session.RunUntilFirstMeal(100);
            Assert.That(session.RecruitCook().Success, Is.False);
            Assert.That(session.GetSnapshot().Tick, Is.EqualTo(terminal.Tick));
            Assert.That(session.GetSnapshot().MealsConsumed, Is.EqualTo(1));
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(terminalLedger));
        }

        [Test]
        public void UnrecruitedAdvanceDoesNotStartAHiddenSimulationOrPayment()
        {
            BaseSession session = new BaseSession(_scenario);
            string before = session.ExportLedgerCsv();
            session.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            Assert.That(session.GetSnapshot().Tick, Is.Zero);
            Assert.That(session.GetSnapshot().IsCookRecruited, Is.False);
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(before));
        }

        [Test]
        public void InsufficientTreasuryRejectsRecruitmentWithoutPartialPayment()
        {
            _scenario.initialTreasuryCash = _scenario.recruitmentGrant - 1;
            BaseSession session = new BaseSession(_scenario);
            string before = session.ExportLedgerCsv();
            BaseCommandResult result = session.RecruitCook();
            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.Not.Empty);
            Assert.That(session.GetSnapshot().IsCookRecruited, Is.False);
            Assert.That(session.GetSnapshot().TreasuryCash, Is.EqualTo(_scenario.initialTreasuryCash));
            Assert.That(session.GetSnapshot().CookCash, Is.EqualTo(_scenario.initialCookCash));
            AssertOwnership(session.GetSnapshot(), 1, 0, 0, 0);
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(before));
        }

        [Test]
        public void InsufficientCookCashDoesNotTakeGrainOrCreditSupplier()
        {
            _scenario.recruitmentGrant = _scenario.grainUnitPrice * _scenario.grainPerMeal - 1;
            BaseSession session = new BaseSession(_scenario);
            session.RecruitCook();
            string afterRecruitment = session.ExportLedgerCsv();
            BaseSnapshot blocked = session.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            Assert.That(blocked.IsComplete, Is.False);
            Assert.That(blocked.CookPosition, Is.EqualTo(_scenario.supplierStart));
            Assert.That(blocked.CookBlockedReason, Does.Contain("资金不足"));
            AssertBalances(blocked, 95, 10, 5, 20);
            AssertOwnership(blocked, 1, 0, 0, 0);
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(afterRecruitment));
        }

        [Test]
        public void MissingSupplierInventoryDoesNotDebitCookOrInventIngredient()
        {
            _scenario.initialSupplierGrain = 0;
            BaseSession session = new BaseSession(_scenario);
            session.RecruitCook();
            string afterRecruitment = session.ExportLedgerCsv();
            BaseSnapshot blocked = session.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            Assert.That(blocked.IsComplete, Is.False);
            Assert.That(blocked.CookBlockedReason, Does.Contain("库存不足"));
            AssertBalances(blocked, 88, 10, 12, 20);
            AssertOwnership(blocked, 0, 0, 0, 0);
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(afterRecruitment));
        }

        [Test]
        public void InsufficientCustomerCashLeavesTheMealOwnedByCookWithoutPartialSale()
        {
            _scenario.initialCustomerCash = _scenario.mealUnitPrice - 1;
            BaseSession session = new BaseSession(_scenario);
            session.RecruitCook();
            BaseSnapshot blocked = session.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            Assert.That(blocked.IsComplete, Is.False);
            Assert.That(blocked.FirstMealTick, Is.EqualTo(-1));
            Assert.That(blocked.CustomerBlockedReason, Does.Contain("资金不足"));
            Assert.That(blocked.TreasuryCash, Is.EqualTo(88));
            Assert.That(blocked.SupplierCash, Is.EqualTo(16));
            Assert.That(blocked.CookCash, Is.EqualTo(6));
            Assert.That(blocked.CustomerCash, Is.EqualTo(9));
            Assert.That(blocked.TotalCash, Is.EqualTo(119));
            AssertOwnership(blocked, 0, 0, 1, 0);
            Assert.That(session.Ledger.Count, Is.EqualTo(4));
            string beforeRetry = session.ExportLedgerCsv();
            session.AdvanceTicks(10);
            Assert.That(session.ExportLedgerCsv(), Is.EqualTo(beforeRetry));
        }

        [Test]
        public void IdenticalOpeningAndResetProduceIdenticalOrderedLedger()
        {
            BaseSession first = new BaseSession(_scenario);
            BaseSession second = new BaseSession(_scenario);
            first.RecruitCook();
            second.RecruitCook();
            first.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            second.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            string expected = first.ExportLedgerCsv();
            Assert.That(second.ExportLedgerCsv(), Is.EqualTo(expected));
            Assert.That(second.ExportLedgerText(), Is.EqualTo(first.ExportLedgerText()));
            first.Reset();
            Assert.That(first.GetSnapshot().Tick, Is.Zero);
            Assert.That(first.Ledger.Count, Is.EqualTo(1));
            first.RecruitCook();
            first.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            Assert.That(first.ExportLedgerCsv(), Is.EqualTo(expected));
        }

        [Test]
        public void KitchenDistanceOnlyChangesTravelAndFirstMealTime()
        {
            GridPosition originalKitchen = _scenario.kitchenPosition;
            BaseSession near = new BaseSession(_scenario.WithKitchen(_scenario.nearKitchenPosition));
            BaseSession far = new BaseSession(_scenario.WithKitchen(_scenario.farKitchenPosition));
            near.RecruitCook();
            far.RecruitCook();
            BaseSnapshot nearDone = near.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            BaseSnapshot farDone = far.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            Assert.That(_scenario.kitchenPosition, Is.EqualTo(originalKitchen), "Selecting a location must copy the opening.");
            Assert.That(nearDone.FirstMealTick, Is.EqualTo(7));
            Assert.That(farDone.FirstMealTick, Is.EqualTo(17));
            Assert.That(nearDone.CookTravelTiles, Is.EqualTo(4));
            Assert.That(farDone.CookTravelTiles, Is.EqualTo(14));
            Assert.That(nearDone.CustomerTravelTiles, Is.EqualTo(3));
            Assert.That(farDone.CustomerTravelTiles, Is.EqualTo(8));
            AssertBalances(nearDone, 88, 16, 16, 10);
            AssertBalances(farDone, 88, 16, 16, 10);
            AssertOwnership(nearDone, 0, 0, 0, 1);
            AssertOwnership(farDone, 0, 0, 0, 1);
        }

        [Test]
        public void ConfiguredMovementAndProductionDurationsChangeTheClockExactly()
        {
            _scenario.movementTicksPerTile = 2;
            _scenario.productionTicks = 4;
            BaseSession session = new BaseSession(_scenario);
            session.RecruitCook();
            BaseSnapshot firstTick = session.Step();
            Assert.That(firstTick.CookPosition, Is.EqualTo(_scenario.kitchenPosition));
            Assert.That(firstTick.CookMovementTicksRemaining, Is.EqualTo(1));
            Assert.That(firstTick.CookTravelTiles, Is.Zero);
            Assert.That(session.Step().CookPosition, Is.EqualTo(new GridPosition(2, 2)));
            BaseSnapshot done = session.RunUntilFirstMeal(_scenario.runUntilFirstMealMaxTicks);
            Assert.That(done.FirstMealTick, Is.EqualTo(12));
            AssertBalances(done, 88, 16, 16, 10);
        }

        [Test]
        public void SessionCopiesInputAndResetDoesNotReadLaterExternalChanges()
        {
            BaseSession session = new BaseSession(_scenario);
            _scenario.initialTreasuryCash = 0;
            _scenario.recruitmentGrant = 1;
            _scenario.kitchenPosition = _scenario.farKitchenPosition;
            Assert.That(session.RecruitCook().Success, Is.True);
            session.RunUntilFirstMeal(100);
            session.Reset();
            AssertBalances(session.GetSnapshot(), 100, 10, 0, 20);
            Assert.That(session.GetSnapshot().KitchenPosition, Is.EqualTo(new GridPosition(3, 2)));
            Assert.That(session.RecruitCook().Success, Is.True);
            Assert.That(session.GetSnapshot().CookCash, Is.EqualTo(12));
        }

        [TestCase("tax")]
        [TestCase("welfare")]
        [TestCase("external")]
        public void UnimplementedNonzeroCashFlowIsRejectedExplicitly(string flow)
        {
            if (flow == "tax") _scenario.taxAmount = 1;
            if (flow == "welfare") _scenario.welfareAmount = 1;
            if (flow == "external") _scenario.externalCashFlow = 1;
            Assert.Throws<NotSupportedException>(() => new BaseSession(_scenario));
        }

        [Test]
        public void InvalidLocationOrZeroDurationCannotSilentlyBecomeAnotherScenario()
        {
            Assert.That(_scenario.IsKitchenPositionAvailable(_scenario.supplierStart), Is.False);
            Assert.That(_scenario.IsKitchenPositionAvailable(new GridPosition(-1, 0)), Is.False);
            Assert.Throws<ArgumentException>(() => _scenario.WithKitchen(_scenario.customerStart));
            _scenario.movementTicksPerTile = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => new BaseSession(_scenario));
        }

        private static void AssertBalances(BaseSnapshot state, int treasury, int supplier, int cook, int customer)
        {
            Assert.That(state.TreasuryCash, Is.EqualTo(treasury));
            Assert.That(state.SupplierCash, Is.EqualTo(supplier));
            Assert.That(state.CookCash, Is.EqualTo(cook));
            Assert.That(state.CustomerCash, Is.EqualTo(customer));
            Assert.That(state.TotalCash, Is.EqualTo(130));
        }

        private static void AssertOwnership(BaseSnapshot state, int supplierGrain, int cookGrain, int cookMeals, int consumed)
        {
            Assert.That(state.SupplierGrain, Is.EqualTo(supplierGrain));
            Assert.That(state.CookGrain, Is.EqualTo(cookGrain));
            Assert.That(state.CookMeals, Is.EqualTo(cookMeals));
            Assert.That(state.MealsConsumed, Is.EqualTo(consumed));
        }
    }
}
