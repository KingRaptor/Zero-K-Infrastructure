namespace ZkData
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    [Table("StructureType")]
    public partial class StructureType
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public StructureType()
        {
            PlanetStructures = new HashSet<PlanetStructure>();
        }

        public int StructureTypeID { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; }

        [StringLength(250)]
        public string Description { get; set; }

        [StringLength(50)]
        public string IngameUnitName { get; set; }

        [StringLength(50)]
        public string MapIcon { get; set; }

        [StringLength(50)]
        public string DisabledMapIcon { get; set; }

        public double? UpkeepEnergy { get; set; }

        public int? TurnsToActivate { get; set; }

        public double? EffectDropshipProduction { get; set; }

        public int? EffectDropshipCapacity { get; set; }

        public double? EffectBomberProduction { get; set; }

        public int? EffectBomberCapacity { get; set; }

        public double? EffectInfluenceSpread { get; set; }

        public int? EffectUnlockID { get; set; }

        public double? EffectEnergyPerTurn { get; set; }

        public bool? EffectIsVictoryPlanet { get; set; }

        public double? EffectWarpProduction { get; set; }

        public bool? EffectAllowShipTraversal { get; set; }

        public double? EffectDropshipDefense { get; set; }

        public double? EffectBomberDefense { get; set; }

        [StringLength(100)]
        public string EffectBots { get; set; }

        public bool? EffectBlocksInfluenceSpread { get; set; }

        public bool? EffectBlocksJumpgate { get; set; }

        public double? EffectRemoteInfluenceSpread { get; set; }

        public bool? EffectCreateLink { get; set; }

        public bool? EffectChangePlanetMap { get; set; }

        public bool? EffectPlanetBuster { get; set; }

        public double Cost { get; set; }

        public bool IsBuildable { get; set; }

        public bool IsIngameDestructible { get; set; }

        public bool IsBomberDestructible { get; set; }

        public bool OwnerChangeDeletesThis { get; set; }

        public bool OwnerChangeDisablesThis { get; set; }

        public bool BattleDeletesThis { get; set; }

        public bool IsSingleUse { get; set; }

        public bool RequiresPlanetTarget { get; set; }

        public double? EffectReduceBattleInfluenceGain { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<PlanetStructure> PlanetStructures { get; set; }

        public virtual Unlock Unlock { get; set; }
    }
}
