using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace TORCareerUniques
{
    /// <summary>
    /// A native bandit party component with a persistent Hero leader.
    /// Keeping BanditPartyComponent as the base preserves Bannerlord's
    /// IsBandit flag and standard bandit campaign-AI path.
    /// </summary>
    public sealed class EncounterBanditPartyComponent : BanditPartyComponent
    {
        [SaveableField(4)]
        private Hero _leader;

        private EncounterBanditPartyComponent(Settlement relatedSettlement)
            : base(relatedSettlement, null)
        {
        }

        public override Hero Leader
        {
            get { return _leader; }
        }

        public override Hero PartyOwner
        {
            get { return _leader; }
        }

        protected override void OnChangePartyLeader(Hero newLeader)
        {
            _leader = newLeader;
            ClearCachedName();
        }

        internal static void Convert(MobileParty party, Settlement relatedSettlement)
        {
            if (party == null)
                throw new System.ArgumentNullException("party");
            if (relatedSettlement == null)
                throw new System.ArgumentNullException("relatedSettlement");

            if (!(party.PartyComponent is EncounterBanditPartyComponent))
                party.SetPartyComponent(
                    new EncounterBanditPartyComponent(relatedSettlement), true);
        }
    }

    /// <summary>
    /// Stable save-system registration for the leader-capable bandit component.
    /// The base ID is derived from the module ID and must remain unchanged.
    /// </summary>
    public sealed class TORCareerUniquesSaveableTypeDefiner : SaveableTypeDefiner
    {
        public TORCareerUniquesSaveableTypeDefiner()
            : base(190216804)
        {
        }

        protected override void DefineClassTypes()
        {
            MethodInfo[] methods = GetType().GetMethods(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "AddClassDefinition")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2 ||
                    parameters[0].ParameterType != typeof(System.Type) ||
                    parameters[1].ParameterType != typeof(int))
                    continue;

                object[] arguments = new object[parameters.Length];
                arguments[0] = typeof(EncounterBanditPartyComponent);
                arguments[1] = 1;
                method.Invoke(this, arguments);
                return;
            }

            throw new System.MissingMethodException(
                "No compatible SaveableTypeDefiner.AddClassDefinition overload was found.");
        }
    }
}
