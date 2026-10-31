using System.Text.Json.Nodes;
using Valkaris.Core.Data;

namespace Valkaris.Core.Tests;

public class GameDataValidationTests
{
    private static void AssertInvalid(string file, Action<JsonNode> mutate, string expectedField)
    {
        var ex = Assert.Throws<GameDataException>(() => TestData.Mutated(file, mutate));

        Assert.Contains(file, ex.Message);
        Assert.Contains(expectedField, ex.Message);
    }

    private static void AssertBuildingsInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.BuildingsFile, mutate, expectedField);

    private static void AssertGameInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.GameFile, mutate, expectedField);

    [Fact]
    public void ShippedDataIsValid() => Assert.NotNull(TestData.Shipped);

    [Fact]
    public void MissingBuilding() =>
        AssertBuildingsInvalid(r => r["buildings"]!.AsObject().Remove("Wall"), "buildings.Wall");

    [Fact]
    public void MaxLevelBelowOne() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["maxLevel"] = 0, "buildings.Wall.maxLevel");

    [Fact]
    public void NegativeCost() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["cost"]!["base"]!["wood"] = -1, "buildings.Wall.cost.base");

    [Fact]
    public void ZeroCost() =>
        AssertBuildingsInvalid(
            r => r["buildings"]!["Wall"]!["cost"]!["base"] = new JsonObject { ["wood"] = 0, ["stone"] = 0, ["food"] = 0 },
            "buildings.Wall.cost.base");

    [Fact]
    public void CostGrowthNotIncreasing() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["cost"]!["growth"] = 1.0, "buildings.Wall.cost.growth");

    [Fact]
    public void NonPositiveBuildTime() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["buildTime"]!["base"] = 0, "buildings.Wall.buildTime.base");

    [Fact]
    public void BuildTimeGrowthNotIncreasing() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Wall"]!["buildTime"]!["growth"] = 0.9, "buildings.Wall.buildTime.growth");

    [Fact]
    public void NonPositiveProduction() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Farm"]!["production"]!["base"] = 0, "buildings.Farm.production.base");

    [Fact]
    public void ResourceWithoutProducer() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Farm"]!["production"]!["resource"] = "Wood", "exactly one producer of Food");

    [Fact]
    public void WarehouseWithoutCapacity() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Warehouse"]!.AsObject().Remove("capacity"), "buildings.Warehouse.capacity");

    [Fact]
    public void NonPositiveCapacityAtLevelZero() =>
        AssertBuildingsInvalid(r => r["buildings"]!["Warehouse"]!["capacity"]!["atLevelZero"] = 0, "buildings.Warehouse.capacity.atLevelZero");

    [Fact]
    public void MainLevelTableWrongLength() =>
        AssertBuildingsInvalid(r => r["maxOtherBuildingLevelByMainLevel"]!.AsArray().RemoveAt(0), "maxOtherBuildingLevelByMainLevel");

    [Fact]
    public void NegativeStartingResources() =>
        AssertGameInvalid(r => r["startingResources"]!["wood"] = -1, "startingResources");

    [Fact]
    public void StartingResourcesAboveCapacity() =>
        AssertGameInvalid(r => r["startingResources"]!["stone"] = 1_000_000, "startingResources");

    [Fact]
    public void MissingStartingBuilding() =>
        AssertGameInvalid(r => r["startingBuildings"]!.AsObject().Remove("Wall"), "startingBuildings.Wall");

    [Fact]
    public void StartingBuildingAboveMaxLevel() =>
        AssertGameInvalid(r => r["startingBuildings"]!["Wall"] = 99, "startingBuildings.Wall");

    [Fact]
    public void QueueLengthBelowOne() =>
        AssertGameInvalid(r => r["maxBuildQueueLength"] = 0, "maxBuildQueueLength");

    [Fact]
    public void MaxPlayersBelowMinPlayers() =>
        AssertGameInvalid(r => r["maxPlayers"] = 1, "maxPlayers");

    [Fact]
    public void EmptyMatchDurations() =>
        AssertGameInvalid(r => r["matchDurationsMinutes"] = new JsonArray(), "matchDurationsMinutes");

    [Fact]
    public void NonPositiveSpeedMultiplier() =>
        AssertGameInvalid(r => r["speedMultipliers"]![0] = 0, "speedMultipliers");

    [Fact]
    public void MissingMapSize() =>
        AssertGameInvalid(r => r["mapSizes"]!.AsObject().Remove("Medium"), "mapSizes.Medium");

    private static void AssertUnitsInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.UnitsFile, mutate, expectedField);

    private static void AssertCombatInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.CombatFile, mutate, expectedField);

    [Fact]
    public void NegativeStartingTroops() =>
        AssertGameInvalid(r => r["startingTroops"]!["Spearman"] = -1, "startingTroops.Spearman");

    [Fact]
    public void TrainingQueueLengthBelowOne() =>
        AssertGameInvalid(r => r["maxTrainingQueueLength"] = 0, "maxTrainingQueueLength");

    [Fact]
    public void NegativeTrainingBonus() =>
        AssertUnitsInvalid(r => r["trainingSpeedBonusPerLevel"] = -0.1, "trainingSpeedBonusPerLevel");

    [Fact]
    public void MissingUnit() =>
        AssertUnitsInvalid(r => r["units"]!.AsObject().Remove("Cavalry"), "units.Cavalry");

    [Theory]
    [InlineData("attack", 0)]
    [InlineData("defenceVsInfantry", -1)]
    [InlineData("defenceVsCavalry", -1)]
    [InlineData("speed", 0)]
    [InlineData("lootCapacity", -1)]
    [InlineData("trainingSeconds", 0)]
    [InlineData("requiredBarracksLevel", 0)]
    [InlineData("requiredBarracksLevel", 99)]
    public void InvalidUnitValue(string field, double value) =>
        AssertUnitsInvalid(r => r["units"]!["Cavalry"]![field] = value, $"units.Cavalry.{field}");

    [Fact]
    public void NegativeUnitCost() =>
        AssertUnitsInvalid(r => r["units"]!["Cavalry"]!["cost"]!["food"] = -1, "units.Cavalry.cost");

    [Fact]
    public void ZeroUnitCost() =>
        AssertUnitsInvalid(
            r => r["units"]!["Cavalry"]!["cost"] = new JsonObject { ["wood"] = 0, ["stone"] = 0, ["food"] = 0 },
            "units.Cavalry.cost");

    [Fact]
    public void UnknownUnitClass()
    {
        var ex = Assert.Throws<GameDataException>(() =>
            TestData.Mutated(GameData.UnitsFile, r => r["units"]!["Cavalry"]!["class"] = "Dragon"));

        Assert.Contains(GameData.UnitsFile, ex.Message);
    }

    [Theory]
    [InlineData("wallBonusPerLevel", -0.1)]
    [InlineData("casualtyExponent", 0)]
    [InlineData("protectedResourceShare", -0.1)]
    [InlineData("protectedResourceShare", 1.1)]
    [InlineData("catapultsPerLevelOfDamage", 0)]
    public void InvalidCombatValue(string field, double value) =>
        AssertCombatInvalid(r => r[field] = value, field);

    [Fact]
    public void NegativeWatchtowerBonus() => AssertCombatInvalid(r => r["scouting"]!["watchtowerBonusPerLevel"] = -0.1, "scouting.watchtowerBonusPerLevel");

    [Theory]
    [InlineData("clearRatio", 0)]
    [InlineData("overwhelmingRatio", 1)]
    [InlineData("troopRangeShare", -0.1)]
    [InlineData("troopRangeMinWidth", 0)]
    [InlineData("wallRangeWidth", 0)]
    public void InvalidScoutingValue(string field, double value) =>
        AssertCombatInvalid(r => r["scouting"]![field] = value, $"scouting.{field}");

    [Fact]
    public void EmptyScoutCurve() => AssertCombatInvalid(r => r["scouting"]!["successCurve"] = new JsonArray(), "scouting.successCurve");

    [Fact]
    public void ScoutCurveRatiosMustRise() => AssertCombatInvalid(r => r["scouting"]!["successCurve"]![1]!["ratio"] = 0.1, "scouting.successCurve");

    [Fact]
    public void ScoutCurveChancesMustNotFall() => AssertCombatInvalid(r => r["scouting"]!["successCurve"]![1]!["chance"] = 0.01, "scouting.successCurve");

    [Fact]
    public void ScoutCurveChancesAreAtMostOne() => AssertCombatInvalid(r => r["scouting"]!["successCurve"]![5]!["chance"] = 1.5, "scouting.successCurve");

    private static void AssertMapInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.MapFile, mutate, expectedField);

    [Fact]
    public void MissingBarbarianCount() =>
        AssertMapInvalid(r => r["barbarianCounts"]!.AsObject().Remove("Medium"), "barbarianCounts.Medium");

    [Fact]
    public void NegativeBarbarianCount() =>
        AssertMapInvalid(r => r["barbarianCounts"]!["Small"] = -1, "barbarianCounts.Small");

    [Theory]
    [InlineData("minVillageSpacing", 0)]
    [InlineData("startJitter", -1)]
    [InlineData("startClearRadius", -1)]
    public void InvalidMapValue(string field, int value) =>
        AssertMapInvalid(r => r[field] = value, field);

    [Theory]
    [InlineData("noiseCellSize", 0)]
    [InlineData("waterBelow", 1.5)]
    [InlineData("hillsAbove", 0.1)]
    [InlineData("forestAbove", -0.1)]
    public void InvalidTerrainValue(string field, double value) =>
        AssertMapInvalid(r => r["terrain"]![field] = value, $"terrain.{field}");

    [Theory]
    [InlineData("resourceRegenPerSecond")]
    [InlineData("troopRegenSeconds")]
    public void NonPositiveRegenRate(string field) =>
        AssertMapInvalid(r => r["barbarians"]![field] = 0, $"barbarians.{field}");

    [Fact]
    public void NoBarbarianTiers() =>
        AssertMapInvalid(r => r["barbarians"]!["tiers"] = new JsonArray(), "barbarians.tiers");

    [Fact]
    public void BarbarianTierWeightBelowOne() =>
        AssertMapInvalid(r => r["barbarians"]!["tiers"]![0]!["weight"] = 0, "tiers[0].weight");

    [Fact]
    public void NegativeBarbarianGarrison() =>
        AssertMapInvalid(r => r["barbarians"]!["tiers"]![0]!["garrison"]!["Spearman"] = -1, "tiers[0].garrison");

    [Fact]
    public void NegativeBarbarianResources() =>
        AssertMapInvalid(r => r["barbarians"]!["tiers"]![0]!["resources"]!["wood"] = -1, "tiers[0].resources");

    [Fact]
    public void BarbarianBuildingAboveMaxLevel() =>
        AssertMapInvalid(r => r["barbarians"]!["tiers"]![0]!["buildings"]!["Wall"] = 99, "tiers[0].buildings.Wall");

    [Fact]
    public void TooFewPlayerVillageNames() =>
        AssertMapInvalid(r => r["playerVillageNames"] = new JsonArray("One"), "playerVillageNames");

    [Fact]
    public void TooFewBarbarianVillageNames() =>
        AssertMapInvalid(r => r["barbarianVillageNames"] = new JsonArray("One"), "barbarianVillageNames");

    private static void AssertVillagesInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.VillagesFile, mutate, expectedField);

    private static void AssertScoringInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.ScoringFile, mutate, expectedField);

    private static void AssertAiInvalid(Action<JsonNode> mutate, string expectedField) =>
        AssertInvalid(GameData.AiFile, mutate, expectedField);

    [Fact]
    public void TooFewAiNames() => AssertAiInvalid(r => r["names"] = new JsonArray("One"), "names");

    [Fact]
    public void WarehouseFullShareOutOfRange() => AssertAiInvalid(r => r["warehouseFullShare"] = 1.5, "warehouseFullShare");

    [Fact]
    public void TrainingBatchBelowOne() => AssertAiInvalid(r => r["trainingBatchMax"] = 0, "trainingBatchMax");

    [Fact]
    public void NegativeCavalryRatio() => AssertAiInvalid(r => r["cavalryPerSwordsman"] = -1, "cavalryPerSwordsman");

    [Fact]
    public void ReinforceShareOutOfRange() => AssertAiInvalid(r => r["reinforceShare"] = 0, "reinforceShare");

    [Theory]
    [InlineData("lordsWanted")]
    [InlineData("capitalMainLevelWeight")]
    [InlineData("lordMinAttack")]
    [InlineData("catapultsWanted")]
    [InlineData("fightersBeforeSaving")]
    public void NegativeAiCount(string field) => AssertAiInvalid(r => r[field] = -1, field);

    [Fact]
    public void MarketplaceLevelOutOfRange() => AssertAiInvalid(r => r["marketplaceLevel"] = 99, "marketplaceLevel");

    [Fact]
    public void DonorKeepShareOutOfRange() => AssertAiInvalid(r => r["donorKeepShare"] = 2, "donorKeepShare");

    [Fact]
    public void NegativeRaidMinLoot() => AssertAiInvalid(r => r["raidMinLoot"] = -1, "raidMinLoot");

    [Fact]
    public void EmptyBuildOrder() => AssertAiInvalid(r => r["buildOrder"] = new JsonArray(), "buildOrder");

    [Fact]
    public void BuildOrderAboveMaxLevel() => AssertAiInvalid(r => r["buildOrder"]![0]!["level"] = 99, "buildOrder[0].level");

    [Fact]
    public void MissingBarbarianPowerBelief() =>
        AssertAiInvalid(r => r["beliefs"]!["barbarianPowerByTier"]!.AsObject().Remove("Camp"), "barbarianPowerByTier.Camp");

    [Fact]
    public void MissingBarbarianLootBelief() =>
        AssertAiInvalid(r => r["beliefs"]!["barbarianLootByTier"]!.AsObject().Remove("Camp"), "barbarianLootByTier.Camp");

    [Theory]
    [InlineData("playerPowerAtStart")]
    [InlineData("playerPowerPerMinute")]
    [InlineData("growthAfterSightingPerMinute")]
    [InlineData("barbarianLootRecoveryPerMinute")]
    public void NegativeBelief(string field) => AssertAiInvalid(r => r["beliefs"]![field] = -1, $"beliefs.{field}");

    [Fact]
    public void ArmyLimitBelowTheArmyWanted() => AssertAiInvalid(r => r["difficulties"]!["Easy"]!["maxFightersPerMinute"] = 0.01, "difficulties.Easy.maxFightersPerMinute");

    [Theory]
    [InlineData("rivalDistanceExponent", -1)]
    [InlineData("rivalSwitchCooldownSeconds", -1)]
    [InlineData("capitalTargetPenalty", 0.5)]
    public void InvalidWarValue(string field, double value) => AssertAiInvalid(r => r[field] = value, field);

    [Theory]
    [InlineData("maxScoutForce", 0)]
    [InlineData("scoutMargin", 0.5)]
    [InlineData("maxWatchtowerLevel", -1)]
    [InlineData("homeScoutsAtStart", -1)]
    [InlineData("homeScoutsPerMinute", -1)]
    [InlineData("maxHomeScouts", -1)]
    public void InvalidScoutProfileValue(string field, double value) =>
        AssertAiInvalid(r => r["difficulties"]!["Normal"]![field] = value, field);

    [Fact]
    public void ScoutRetryNegative() => AssertAiInvalid(r => r["scoutRetrySeconds"] = -1, "scoutRetrySeconds");

    [Fact]
    public void ScoutedAllowanceBelowOne() => AssertAiInvalid(r => r["beliefs"]!["scoutedPlayerAllowance"] = 0.5, "beliefs.scoutedPlayerAllowance");

    [Fact]
    public void AbandonedPowerNotPositive() => AssertAiInvalid(r => r["beliefs"]!["abandonedPower"] = 0, "beliefs.abandonedPower");

    [Fact]
    public void AbandonedLootNegative() => AssertAiInvalid(r => r["beliefs"]!["abandonedLoot"] = -1, "beliefs.abandonedLoot");

    [Fact]
    public void PowerPerTroopNotPositive() => AssertAiInvalid(r => r["beliefs"]!["powerPerTroop"] = 0, "beliefs.powerPerTroop");

    [Fact]
    public void MissingDifficulty() => AssertAiInvalid(r => r["difficulties"]!.AsObject().Remove("Hard"), "difficulties.Hard");

    [Theory]
    [InlineData("decisionSeconds", 0)]
    [InlineData("fightersPerMinute", -1)]
    [InlineData("raidMargin", 0.5)]
    [InlineData("attackMargin", 0.5)]
    [InlineData("lordMargin", 0.5)]
    [InlineData("aggressionDelaySeconds", -1)]
    [InlineData("garrisonShare", 1.5)]
    [InlineData("maxRaidsPerVillage", -1)]
    [InlineData("raidCooldownSeconds", -1)]
    [InlineData("wantedVillages", 0)]
    [InlineData("expansionDelaySeconds", -1)]
    [InlineData("scoutMaxAgeSeconds", -1)]
    [InlineData("maxWallLevel", -1)]
    [InlineData("rivalFocusShare", 1.5)]
    [InlineData("rivalStrongerRatio", 0.5)]
    [InlineData("massSeconds", -1)]
    [InlineData("surrenderVillageShare", 1.5)]
    [InlineData("surrenderStrengthRatio", -1)]
    [InlineData("surrenderHoldSeconds", -1)]
    public void InvalidProfileValue(string field, double value) =>
        AssertAiInvalid(r => r["difficulties"]!["Easy"]![field] = field is "maxRaidsPerVillage" or "wantedVillages" or "maxWallLevel" ? (int)value : value, $"difficulties.Easy.{field}");

    [Fact]
    public void ShortCarrierTable() =>
        AssertBuildingsInvalid(r => r["carriersByMarketplaceLevel"] = new JsonArray(0, 2), "carriersByMarketplaceLevel");

    [Fact]
    public void NegativeCarrierCount() =>
        AssertBuildingsInvalid(r => r["carriersByMarketplaceLevel"]![1] = -1, "carriersByMarketplaceLevel");

    [Fact]
    public void CarrierCapacityBelowOne() =>
        AssertVillagesInvalid(r => r["carriers"]!["capacity"] = 0, "carriers.capacity");

    [Fact]
    public void CarrierSpeedNotPositive() =>
        AssertVillagesInvalid(r => r["carriers"]!["speed"] = 0, "carriers.speed");

    [Fact]
    public void ShortVillageLimitTable() =>
        AssertBuildingsInvalid(r => r["maxVillagesByCapitalMainLevel"] = new JsonArray(1, 2), "maxVillagesByCapitalMainLevel");

    [Fact]
    public void VillageLimitBelowOne() =>
        AssertBuildingsInvalid(r => r["maxVillagesByCapitalMainLevel"]![0] = 0, "maxVillagesByCapitalMainLevel");

    [Fact]
    public void FoundedVillageNegativeResources() =>
        AssertGameInvalid(r => r["foundedVillage"]!["resources"]!["wood"] = -1, "foundedVillage.resources");

    [Fact]
    public void FoundedVillageMissingBuilding() =>
        AssertGameInvalid(r => r["foundedVillage"]!["buildings"]!.AsObject().Remove("Wall"), "foundedVillage.buildings.Wall");

    [Fact]
    public void FoundedVillageBuildingAboveMaxLevel() =>
        AssertGameInvalid(r => r["foundedVillage"]!["buildings"]!["Wall"] = 99, "foundedVillage.buildings.Wall");

    [Fact]
    public void FoundedVillageNegativeTroops() =>
        AssertGameInvalid(r => r["foundedVillage"]!["troops"]!["Spearman"] = -1, "foundedVillage.troops");

    [Fact]
    public void FoundingDistanceBelowOne() =>
        AssertMapInvalid(r => r["minFoundingDistance"] = 0, "minFoundingDistance");

    [Fact]
    public void NoFoundedVillageNames() =>
        AssertMapInvalid(r => r["foundedVillageNames"] = new JsonArray(), "foundedVillageNames");

    [Fact]
    public void LoyaltyDropMinBelowOne() =>
        AssertVillagesInvalid(r => r["loyalty"]!["dropMin"] = 0, "loyalty.dropMin");

    [Fact]
    public void LoyaltyDropMaxBelowMin() =>
        AssertVillagesInvalid(r => r["loyalty"]!["dropMax"] = 1, "loyalty.dropMax");

    [Fact]
    public void LoyaltyAfterCaptureOutOfRange() =>
        AssertVillagesInvalid(r => r["loyalty"]!["afterCapture"] = 101, "loyalty.afterCapture");

    [Fact]
    public void NegativeLoyaltyRegen() =>
        AssertVillagesInvalid(r => r["loyalty"]!["regenPerSecond"] = -1, "loyalty.regenPerSecond");

    [Fact]
    public void NegativeLoyaltyRegenPerLevel() =>
        AssertVillagesInvalid(r => r["loyalty"]!["regenPerSecondPerMainLevel"] = -1, "loyalty.regenPerSecondPerMainLevel");

    [Fact]
    public void CapitalLockAboveMaxLevel() =>
        AssertVillagesInvalid(r => r["capital"]!["lockedAboveMainLevel"] = 99, "capital.lockedAboveMainLevel");

    [Fact]
    public void CapitalChoiceSecondsNotPositive() =>
        AssertVillagesInvalid(r => r["capital"]!["choiceSeconds"] = 0, "capital.choiceSeconds");

    [Fact]
    public void CapitalPenaltyAboveMaxLevel() =>
        AssertVillagesInvalid(r => r["capital"]!["penaltyMainLevel"] = 99, "capital.penaltyMainLevel");

    [Fact]
    public void NegativeVillageWeight() =>
        AssertScoringInvalid(r => r["villageWeight"] = -1, "villageWeight");

    [Fact]
    public void NegativePopulationWeight() =>
        AssertScoringInvalid(r => r["populationWeight"] = -1, "populationWeight");

    [Fact]
    public void NegativeCaptureWeight() =>
        AssertScoringInvalid(r => r["captureWeight"] = -1, "captureWeight");

    [Fact]
    public void MissingPopulationWeight() =>
        AssertScoringInvalid(r => r["populationPerLevel"]!.AsObject().Remove("Wall"), "populationPerLevel.Wall");

    [Fact]
    public void NegativePopulationPerLevel() =>
        AssertScoringInvalid(r => r["populationPerLevel"]!["Wall"] = -1, "populationPerLevel.Wall");

    [Fact]
    public void AllProblemsAreReportedTogether()
    {
        var ex = Assert.Throws<GameDataException>(() => TestData.Mutated(GameData.BuildingsFile, r =>
        {
            r["buildings"]!["Wall"]!["maxLevel"] = 0;
            r["buildings"]!["Farm"]!["buildTime"]!["base"] = 0;
        }));

        Assert.Contains("buildings.Wall.maxLevel", ex.Message);
        Assert.Contains("buildings.Farm.buildTime.base", ex.Message);
    }
}
