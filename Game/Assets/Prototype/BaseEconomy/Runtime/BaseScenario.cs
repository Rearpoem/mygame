using System;

namespace Prototype.BaseEconomy
{
    [Serializable]
    public struct GridPosition : IEquatable<GridPosition>
    {
        public int x;
        public int y;

        public GridPosition(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public int ManhattanDistance(GridPosition other)
        {
            return Math.Abs(x - other.x) + Math.Abs(y - other.y);
        }

        public bool Equals(GridPosition other)
        {
            return x == other.x && y == other.y;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPosition && Equals((GridPosition)obj);
        }

        public override int GetHashCode()
        {
            unchecked { return (x * 397) ^ y; }
        }

        public override string ToString()
        {
            return "(" + x + ", " + y + ")";
        }

        public static bool operator ==(GridPosition left, GridPosition right) { return left.Equals(right); }
        public static bool operator !=(GridPosition left, GridPosition right) { return !left.Equals(right); }
    }

    /// <summary>
    /// Only temporary first-meal test inputs. Public fields support Unity JsonUtility;
    /// this assembly deliberately has no Unity dependency.
    /// </summary>
    [Serializable]
    public sealed class BaseScenario
    {
        // No C# fallback values: default.json is the single source of test parameters.
        public int schemaVersion;
        public string scenarioId;
        public string testValueNotice;
        public int mapWidth;
        public int mapHeight;
        public GridPosition supplierStart;
        public GridPosition customerStart;
        public GridPosition kitchenPosition;
        public GridPosition nearKitchenPosition;
        public GridPosition farKitchenPosition;
        public int initialTreasuryCash;
        public int initialSupplierCash;
        public int initialCookCash;
        public int initialCustomerCash;
        public int initialSupplierGrain;
        public int recruitmentGrant;
        public int grainUnitPrice;
        public int mealUnitPrice;
        public int grainPerMeal;
        public int movementTicksPerTile;
        public int productionTicks;
        public int taxAmount;
        public int welfareAmount;
        public int externalCashFlow;
        public int runUntilFirstMealMaxTicks;

        public BaseScenario Clone()
        {
            return (BaseScenario)MemberwiseClone();
        }

        public bool IsKitchenPositionAvailable(GridPosition position)
        {
            return IsInsideMap(position) && position != supplierStart && position != customerStart;
        }

        /// <summary>Changes the preplaced facility's opening position, not a construction command.</summary>
        public BaseScenario WithKitchen(GridPosition position)
        {
            if (!IsKitchenPositionAvailable(position))
                throw new ArgumentException("Kitchen must occupy a map tile other than a resident's starting tile.", "position");
            BaseScenario copy = Clone();
            copy.kitchenPosition = position;
            return copy;
        }

        public void Validate()
        {
            if (schemaVersion != 1)
                throw new ArgumentException("Unsupported BaseEconomy scenario schemaVersion.");
            if (mapWidth < 8 || mapHeight < 5)
                throw new ArgumentException("The first-meal test requires a map of at least 8 x 5 tiles.");
            if (!IsInsideMap(supplierStart) || !IsInsideMap(customerStart))
                throw new ArgumentException("Resident starting positions must be inside the map.");
            if (!IsKitchenPositionAvailable(kitchenPosition) ||
                !IsKitchenPositionAvailable(nearKitchenPosition) ||
                !IsKitchenPositionAvailable(farKitchenPosition))
                throw new ArgumentException("Kitchen positions must be inside the map and on unoccupied starting tiles.");
            RequireNonnegative(initialTreasuryCash, "initialTreasuryCash");
            RequireNonnegative(initialSupplierCash, "initialSupplierCash");
            RequireNonnegative(initialCookCash, "initialCookCash");
            RequireNonnegative(initialCustomerCash, "initialCustomerCash");
            RequireNonnegative(initialSupplierGrain, "initialSupplierGrain");
            RequireNonnegative(recruitmentGrant, "recruitmentGrant");
            RequireNonnegative(grainUnitPrice, "grainUnitPrice");
            RequireNonnegative(mealUnitPrice, "mealUnitPrice");
            RequirePositive(grainPerMeal, "grainPerMeal");
            RequirePositive(movementTicksPerTile, "movementTicksPerTile");
            RequirePositive(productionTicks, "productionTicks");
            RequirePositive(runUntilFirstMealMaxTicks, "runUntilFirstMealMaxTicks");
            if (taxAmount != 0 || welfareAmount != 0 || externalCashFlow != 0)
                throw new NotSupportedException("This single-meal prototype supports only zero tax, welfare and external cash flow. These systems are not implemented.");
            long totalCash = (long)initialTreasuryCash + initialSupplierCash + initialCookCash + initialCustomerCash;
            if (totalCash > int.MaxValue || (long)grainUnitPrice * grainPerMeal > int.MaxValue)
                throw new ArgumentException("Test cash amounts exceed the supported integer range.");
        }

        private bool IsInsideMap(GridPosition position)
        {
            return position.x >= 0 && position.x < mapWidth && position.y >= 0 && position.y < mapHeight;
        }

        private static void RequireNonnegative(int value, string name)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(name, "Test value must be nonnegative.");
        }

        private static void RequirePositive(int value, string name)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(name, "Test value must be positive.");
        }
    }
}
