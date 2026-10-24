using System;
using System.Numerics;

namespace CS2.Animation;

// Read by hand rather than generated: these nodes are CS2's own, ported from ValveResourceFormat's renderer.
partial class SnapWeaponNode
{
    /// <summary>Reads the SnapWeaponNode from its KeyValues3 data.</summary>
    public SnapWeaponNode(global::CS2.KVObject data) : base(data)
    {
        if (data.ContainsKey("m_nFlashedAmountNodeIdx"))
        {
            FlashedAmountNodeIdx = (short)data.GetIntegerProperty("m_nFlashedAmountNodeIdx");
        }

        if (data.ContainsKey("m_nWeaponCategoryNodeIdx"))
        {
            WeaponCategoryNodeIdx = (short)data.GetIntegerProperty("m_nWeaponCategoryNodeIdx");
        }

        if (data.ContainsKey("m_nWeaponTypeNodeIdx"))
        {
            WeaponTypeNodeIdx = (short)data.GetIntegerProperty("m_nWeaponTypeNodeIdx");
        }
    }
}

partial class AimCSNode
{
    /// <summary>Reads the AimCSNode from its KeyValues3 data.</summary>
    public AimCSNode(global::CS2.KVObject data) : base(data)
    {
        if (data.ContainsKey("m_nVerticalAngleNodeIdx"))
        {
            VerticalAngleNodeIdx = (short)data.GetIntegerProperty("m_nVerticalAngleNodeIdx");
        }

        if (data.ContainsKey("m_nHorizontalAngleNodeIdx"))
        {
            HorizontalAngleNodeIdx = (short)data.GetIntegerProperty("m_nHorizontalAngleNodeIdx");
        }

        if (data.ContainsKey("m_nWeaponCategoryNodeIdx"))
        {
            WeaponCategoryNodeIdx = (short)data.GetIntegerProperty("m_nWeaponCategoryNodeIdx");
        }

        if (data.ContainsKey("m_nWeaponTypeNodeIdx"))
        {
            WeaponTypeNodeIdx = (short)data.GetIntegerProperty("m_nWeaponTypeNodeIdx");
        }

        if (data.ContainsKey("m_nWeaponActionNodeIdx"))
        {
            WeaponActionNodeIdx = (short)data.GetIntegerProperty("m_nWeaponActionNodeIdx");
        }

        if (data.ContainsKey("m_nWeaponDropNodeIdx"))
        {
            WeaponDropNodeIdx = (short)data.GetIntegerProperty("m_nWeaponDropNodeIdx");
        }

        if (data.ContainsKey("m_nIsDefusingNodeIdx"))
        {
            IsDefusingNodeIdx = (short)data.GetIntegerProperty("m_nIsDefusingNodeIdx");
        }

        if (data.ContainsKey("m_nCrouchWeightNodeIdx"))
        {
            CrouchWeightNodeIdx = (short)data.GetIntegerProperty("m_nCrouchWeightNodeIdx");
        }

        if (data.ContainsKey("m_flHandIKBlendInTimeSeconds"))
        {
            HandIKBlendInTimeSeconds = data.GetFloatProperty("m_flHandIKBlendInTimeSeconds");
        }

        if (data.ContainsKey("m_flActionBlendTimeSeconds"))
        {
            ActionBlendTimeSeconds = data.GetFloatProperty("m_flActionBlendTimeSeconds");
        }

        if (data.ContainsKey("m_flPlantingBlendTimeSeconds"))
        {
            PlantingBlendTimeSeconds = data.GetFloatProperty("m_flPlantingBlendTimeSeconds");
        }
    }
}
