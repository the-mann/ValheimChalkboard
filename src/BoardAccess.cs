using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ValheimWhiteboard
{
    public static class BoardAccess
    {
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly FieldInfo Wards = typeof(PrivateArea).GetField("m_allAreas", Flags);
        private static readonly MethodInfo Enabled = typeof(PrivateArea).GetMethod("IsEnabled", Flags);
        private static readonly MethodInfo Inside = typeof(PrivateArea).GetMethod("IsInside", Flags);
        private static readonly MethodInfo Permitted = typeof(PrivateArea).GetMethod("IsPermitted", Flags);
        public static bool CanWrite(long sender, Vector3 position)
        {
            foreach (var player in Player.GetAllPlayers())
            {
                var view = player.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && view.GetZDO().GetOwner() == sender && !player.IsDead()
                    && Vector3.Distance(player.transform.position, position) <= 5f && WardAccess(player.GetPlayerID(), position)) return true;
            }
            return false;
        }
        public static bool WardAccess(long playerId, Vector3 position)
        {
            if (Wards == null || Enabled == null || Inside == null || Permitted == null) return false;
            try
            {
                bool protectedArea = false;
                foreach (var ward in (List<PrivateArea>)Wards.GetValue(null))
                {
                    if (ward == null || !(bool)Enabled.Invoke(ward, null) || !(bool)Inside.Invoke(ward, new object[] { position, 0f })) continue;
                    protectedArea = true;
                    var piece = ward.GetComponent<Piece>();
                    if ((piece != null && piece.GetCreator() == playerId) || (bool)Permitted.Invoke(ward, new object[] { playerId })) return true;
                }
                return !protectedArea;
            }
            catch { return false; }
        }
    }
}
