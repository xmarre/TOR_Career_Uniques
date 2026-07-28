using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace TORCareerUniques
{
    internal sealed partial class UniqueEncounterBehavior
    {
        private const int CurrentSetMasterySchemaVersion = 1;

        private Dictionary<string, Hero> _successorHeroes =
            new Dictionary<string, Hero>(StringComparer.Ordinal);
        private List<string> _masteryProvenCareerIds = new List<string>();
        private List<string> _masteryVictoryCareerIds = new List<string>();
        private List<string> _recruitedOriginalCareerIds = new List<string>();
        private List<string> _pendingRecognitionCareerIds = new List<string>();
        private int _setMasterySchemaVersion;

        private bool IsOriginalRecruited(string careerId)
        {
            return _recruitedOriginalCareerIds != null &&
                _recruitedOriginalCareerIds.Contains(careerId);
        }

        private bool IsMasteryProven(string careerId)
        {
            return _masteryProvenCareerIds != null &&
                _masteryProvenCareerIds.Contains(careerId);
        }

        private bool IsRecruitmentEligibilityProven(string careerId)
        {
            return ModConfig.HeroRecruitmentMode > 0 &&
                IsMasteryProven(careerId) &&
                (ModConfig.HeroRecruitmentMode == 1 ||
                 _masteryVictoryCareerIds.Contains(careerId));
        }

        private bool TryGetActiveEncounterHero(string careerId, out Hero hero)
        {
            hero = null;
            if (IsOriginalRecruited(careerId))
                return _successorHeroes.TryGetValue(careerId, out hero) && hero != null;
            return _encounterHeroes.TryGetValue(careerId, out hero) && hero != null;
        }

        private Dictionary<string, Hero> GetActiveEncounterHeroSnapshot()
        {
            Dictionary<string, Hero> result =
                new Dictionary<string, Hero>(StringComparer.Ordinal);
            for (int i = 0; i < EncounterCatalog.All.Length; i++)
            {
                string careerId = EncounterCatalog.All[i].CareerId;
                Hero hero;
                if (TryGetActiveEncounterHero(careerId, out hero))
                    result[careerId] = hero;
            }
            return result;
        }

        private bool IsSuccessorHero(string careerId, Hero hero)
        {
            Hero successor;
            return hero != null && _successorHeroes.TryGetValue(careerId,
                out successor) && Object.ReferenceEquals(hero, successor);
        }

        private EncounterHeroProfile GetProfileForLeader(string careerId, Hero hero)
        {
            EncounterHeroProfile original = EncounterHeroProfiles.Get(careerId);
            if (original == null || !IsSuccessorHero(careerId, hero))
                return original;
            SuccessorIdentity identity = EncounterSuccessorProfiles.Get(careerId);
            return identity == null ? original : identity.ApplyTo(original);
        }

        private bool WasOriginalLeaderDefeated(string careerId, MobileParty party)
        {
            if (party == null || IsOriginalRecruited(careerId))
                return false;
            Hero original;
            return _encounterHeroes.TryGetValue(careerId, out original) &&
                original != null && Object.ReferenceEquals(party.LeaderHero, original);
        }

        private void EvaluateSetMasteryVictory(EncounterDefinition definition,
            MobileParty defeatedParty)
        {
            if (definition == null || ModConfig.HeroRecruitmentMode <= 0 ||
                !WasOriginalLeaderDefeated(definition.CareerId, defeatedParty) ||
                IsMasteryProven(definition.CareerId))
                return;

            int equipped = SetItemRuntime.GetEquippedRealSetPieceCount(
                definition.CareerId);
            bool collectionComplete = SetItemRuntime.IsSetComplete(
                definition.CareerId);
            bool qualifies = collectionComplete &&
                (ModConfig.HeroRecruitmentMode == 1 || equipped == 5);
            if (!qualifies)
                return;

            _masteryProvenCareerIds.Add(definition.CareerId);
            if (equipped == 5 &&
                !_masteryVictoryCareerIds.Contains(definition.CareerId))
                _masteryVictoryCareerIds.Add(definition.CareerId);
            if (equipped == 5 &&
                !_pendingRecognitionCareerIds.Contains(definition.CareerId))
                _pendingRecognitionCareerIds.Add(definition.CareerId);
            AdminBridge.RequestApplicationTick();
            ModLog.Info("Set mastery proven for " + definition.CareerId +
                " against the original hero; equipped pieces=" + equipped +
                ", recruitment mode=" + ModConfig.HeroRecruitmentMode + ".");
        }

        private bool HasPendingRecognition()
        {
            return _pendingRecognitionCareerIds != null &&
                _pendingRecognitionCareerIds.Count > 0;
        }

        private bool ProcessPendingRecognition()
        {
            if (_inquiryOpen || IsPlayerStillInMapEvent() ||
                !HasPendingRecognition())
                return false;

            string careerId = _pendingRecognitionCareerIds[0];
            _pendingRecognitionCareerIds.RemoveAt(0);
            if (IsOriginalRecruited(careerId) ||
                !IsRecruitmentEligibilityProven(careerId))
                return false;
            if (SetItemRuntime.GetEquippedRealSetPieceCount(careerId) != 5)
            {
                ModLog.Info("Deferred set-mastery parley for " + careerId +
                    " because the matching full set is no longer equipped.");
                return false;
            }
            ShowRecognitionParley(careerId);
            return true;
        }

        private void ShowRecognitionParley(string careerId)
        {
            RecognitionDialogue dialogue = RecognitionDialogues.Get(careerId);
            Hero original;
            if (dialogue == null || !_encounterHeroes.TryGetValue(careerId,
                out original) || original == null || IsOriginalRecruited(careerId))
                return;

            _inquiryOpen = true;
            bool masteryIncludesVictory = _masteryVictoryCareerIds.Contains(
                careerId);
            string recognitionText = dialogue.Opening + "\n\n" +
                dialogue.SetRecognition +
                (masteryIncludesVictory ? "\n\n" +
                    dialogue.VictoryRecognition : String.Empty) + "\n\n" +
                dialogue.ReasonToJoin;
            bool shown = InquiryHelper.ShowChoice(original.Name.ToString(),
                recognitionText,
                dialogue.InviteChoice, "Leave for now",
                delegate
                {
                    _inquiryOpen = false;
                    ShowRecruitmentDecision(careerId);
                },
                delegate
                {
                    _inquiryOpen = false;
                    InquiryHelper.ShowMessage(original.Name.ToString(),
                        dialogue.PostponeLine);
                });
            if (!shown)
            {
                _inquiryOpen = false;
                if (!_pendingRecognitionCareerIds.Contains(careerId))
                    _pendingRecognitionCareerIds.Add(careerId);
                AdminBridge.RequestApplicationTick();
            }
        }

        private void ShowRecruitmentDecision(string careerId)
        {
            RecognitionDialogue dialogue = RecognitionDialogues.Get(careerId);
            Hero original;
            if (dialogue == null || !_encounterHeroes.TryGetValue(careerId,
                out original) || original == null || IsOriginalRecruited(careerId))
                return;
            _inquiryOpen = true;
            bool shown = InquiryHelper.ShowChoice(original.Name.ToString(),
                dialogue.RecruitmentQuestion,
                "Join my clan", "Not yet",
                delegate
                {
                    _inquiryOpen = false;
                    string error;
                    if (TryRecruitOriginalHero(careerId, out error))
                        InquiryHelper.ShowMessage(original.Name.ToString(),
                            dialogue.AcceptanceLine);
                    else
                        InquiryHelper.ShowMessage("Recruitment unavailable",
                            error ?? "The recruitment transition could not be completed.");
                },
                delegate
                {
                    _inquiryOpen = false;
                    InquiryHelper.ShowMessage(original.Name.ToString(),
                        dialogue.PostponeLine);
                });
            if (!shown)
                _inquiryOpen = false;
        }

        private bool TryRecruitOriginalHero(string careerId, out string error)
        {
            error = null;
            if (ModConfig.HeroRecruitmentMode <= 0)
            {
                error = "Encounter-hero recruitment is disabled in Mod Options.";
                return false;
            }
            if (!IsRecruitmentEligibilityProven(careerId))
            {
                error = ModConfig.HeroRecruitmentMode == 2
                    ? "The original hero has not been defeated while the matching complete set was equipped."
                    : "Mastery has not been proven for this original hero.";
                return false;
            }
            if (SetItemRuntime.GetEquippedRealSetPieceCount(careerId) != 5)
            {
                error = "The matching complete 5/5 set must be equipped on the currently controlled hero.";
                return false;
            }
            if (IsOriginalRecruited(careerId))
            {
                error = "The original hero has already joined the player clan.";
                return false;
            }

            Hero original;
            EncounterDefinition definition;
            if (!_encounterHeroes.TryGetValue(careerId, out original) ||
                original == null ||
                !EncounterCatalog.ByCareer.TryGetValue(careerId, out definition))
            {
                error = "The authoritative original hero reference is unavailable.";
                return false;
            }
            if (original.IsDead || original.IsPrisoner ||
                original.PartyBelongedToAsPrisoner != null)
            {
                error = "The original hero is not currently free and alive.";
                return false;
            }
            MobileParty previousParty = original.PartyBelongedTo;
            if (previousParty != null && previousParty.IsActive)
            {
                error = "The defeated encounter party is still active; wait until the battle aftermath has closed.";
                return false;
            }
            if (Clan.PlayerClan == null || MobileParty.MainParty == null)
            {
                error = "The player clan or main party is unavailable.";
                return false;
            }

            Settlement encounterAnchor = ResolveAnchor(definition);
            Clan nativeBanditClan = ResolveBanditClan(definition);
            Clan encounterClan = null;
            try
            {
                encounterClan = ResolveOrCreateEncounterOwnerClan(definition,
                    encounterAnchor, original, original.CharacterObject,
                    nativeBanditClan);
                Hero successor = GetOrCreateSuccessor(definition,
                    encounterAnchor, nativeBanditClan);
                if (successor == null)
                    throw new InvalidOperationException("Persistent successor creation returned null.");

                if (original.PartyBelongedTo != null)
                    MakeHeroFugitiveAction.Apply(original, false);
                if (original.PartyBelongedTo != null)
                    throw new InvalidOperationException("The original hero could not be detached from the defeated encounter party.");

                _recruitedOriginalCareerIds.Add(careerId);
                _pendingHeroRecoveries.Remove(careerId);
                _pendingRecognitionCareerIds.Remove(careerId);
                EncounterHeroDeathGuard.Unregister(original);
                original.StayingInSettlement = null;
                original.ChangeState(Hero.CharacterStates.Active);
                original.SetNewOccupation(Occupation.Wanderer);
                // A normal wanderer companion has no latent lord-clan membership.
                // Clear the encounter clan before CompanionOf is assigned so later
                // MCC promotion/removal cannot reveal the old bandit clan again.
                original.Clan = null;
                AddCompanionAction.Apply(Clan.PlayerClan, original);
                AddHeroToPartyAction.Apply(original, MobileParty.MainParty, true);

                if (!Object.ReferenceEquals(original.CompanionOf, Clan.PlayerClan) ||
                    !Object.ReferenceEquals(original.Clan, Clan.PlayerClan))
                    throw new InvalidOperationException("Bannerlord did not retain player-clan companion membership.");
                if (!Object.ReferenceEquals(original.PartyBelongedTo,
                    MobileParty.MainParty))
                    throw new InvalidOperationException("Bannerlord did not retain main-party membership.");

                RebuildEncounterHeroDeathGuard();
                ModLog.Info("Recruited original encounter hero " + original.Name +
                    " [" + original.StringId + "] as a genuine player-clan companion; successor=" +
                    successor.Name + " [" + successor.StringId + "].");
                return true;
            }
            catch (Exception ex)
            {
                // If native companion assignment already committed, retaining the
                // recruited flag is the only safe rollback: encounter reconciliation
                // must never reclaim a partially transitioned player companion.
                if (Object.ReferenceEquals(original.CompanionOf, Clan.PlayerClan))
                {
                    if (!_recruitedOriginalCareerIds.Contains(careerId))
                        _recruitedOriginalCareerIds.Add(careerId);
                    EncounterHeroDeathGuard.Unregister(original);
                }
                else
                {
                    _recruitedOriginalCareerIds.Remove(careerId);
                    if (encounterClan != null)
                        EnsureEncounterHeroClan(original, encounterClan);
                    EncounterHeroDeathGuard.Register(original);
                }
                RebuildEncounterHeroDeathGuard();
                error = FormatException(ex);
                ModLog.Error("Original-hero recruitment transition failed for " +
                    careerId + ": " + error);
                return false;
            }
        }

        private Hero GetOrCreateSuccessor(EncounterDefinition definition,
            TaleWorlds.CampaignSystem.Settlements.Settlement anchor,
            Clan partyClan)
        {
            Hero successor;
            if (_successorHeroes.TryGetValue(definition.CareerId,
                out successor) && successor != null)
            {
                Clan ownerClan = ResolveOrCreateEncounterOwnerClan(definition,
                    anchor, successor, successor.CharacterObject, partyClan);
                string auditError;
                if (!AuditPersistentHero(definition.CareerId, successor,
                    ownerClan, out auditError))
                    throw new InvalidOperationException(
                        "Existing persistent successor failed validation: " +
                        auditError);
                EncounterHeroDeathGuard.Register(successor);
                return successor;
            }

            // GetOrCreateEncounterHero selects the successor map while this temporary
            // marker is present. The marker is removed if construction fails.
            bool addedMarker = !_recruitedOriginalCareerIds.Contains(
                definition.CareerId);
            if (addedMarker)
                _recruitedOriginalCareerIds.Add(definition.CareerId);
            try
            {
                successor = GetOrCreateEncounterHero(definition, anchor, partyClan);
                return successor;
            }
            finally
            {
                if (addedMarker)
                    _recruitedOriginalCareerIds.Remove(definition.CareerId);
            }
        }

        private void RebuildEncounterHeroDeathGuard()
        {
            EncounterHeroDeathGuard.ClearAndRegister(
                GetActiveEncounterHeroSnapshot().Values);
        }

        private string GetMasteryOverview(string careerId)
        {
            if (IsOriginalRecruited(careerId))
            {
                Hero successor;
                string successorState = _successorHeroes.TryGetValue(careerId,
                    out successor) && successor != null
                    ? successor.Name + " assigned"
                    : "successor not yet created";
                return "Original hero recruited; " + successorState;
            }
            if (ModConfig.HeroRecruitmentMode <= 0)
                return "Recruitment disabled in Mod Options";
            if (!IsMasteryProven(careerId))
                return SetItemRuntime.IsSetComplete(careerId)
                    ? "Complete set recovered — mastery challenge available"
                    : "Set incomplete";
            if (!IsRecruitmentEligibilityProven(careerId))
                return "Complete set recognized — qualifying final victory required";
            return SetItemRuntime.GetEquippedRealSetPieceCount(careerId) == 5
                ? "Mastery proven — recruitment available"
                : "Mastery proven — equip the complete set to parley";
        }

        private void UnlockFullSetOnlyMasteryOnDemand(string careerId)
        {
            if (ModConfig.HeroRecruitmentMode != 1 ||
                IsOriginalRecruited(careerId) || IsMasteryProven(careerId) ||
                !SetItemRuntime.IsSetComplete(careerId) ||
                SetItemRuntime.GetEquippedRealSetPieceCount(careerId) != 5)
                return;
            _masteryProvenCareerIds.Add(careerId);
            ModLog.Info("Set-only recruitment eligibility unlocked on demand for " +
                careerId + ". No qualifying final victory was required by MCM mode.");
        }

        private void MigrateSetMasteryState()
        {
            if (_setMasterySchemaVersion >= CurrentSetMasterySchemaVersion)
                return;
            // v1.6.4 has no recruitment state. Existing original references and every
            // progression/cooldown collection remain authoritative and untouched.
            _setMasterySchemaVersion = CurrentSetMasterySchemaVersion;
            ModLog.Info("Set-mastery save state initialized at schema " +
                CurrentSetMasterySchemaVersion + "; existing original heroes preserved.");
        }
    }

    internal sealed class SuccessorIdentity
    {
        internal string FullName;
        internal string FirstName;

        internal EncounterHeroProfile ApplyTo(EncounterHeroProfile source)
        {
            return new EncounterHeroProfile
            {
                CareerId = source.CareerId,
                FullName = FullName,
                FirstName = FirstName,
                Level = source.Level,
                Age = source.Age,
                PreferMounted = source.PreferMounted,
                RequireMounted = source.RequireMounted,
                IsCaster = source.IsCaster,
                MaxSelectedSpells = source.MaxSelectedSpells,
                RequiredTemplateTokens = source.RequiredTemplateTokens,
                TemplateTokens = source.TemplateTokens,
                NegativeTemplateTokens = source.NegativeTemplateTokens,
                BranchTokens = source.BranchTokens,
                AbilityTokens = source.AbilityTokens,
                PrimarySkillTokens = source.PrimarySkillTokens,
                SecondarySkillTokens = source.SecondarySkillTokens
            };
        }
    }

    internal static class EncounterSuccessorProfiles
    {
        private static readonly Dictionary<string, SuccessorIdentity> ByCareer = Build();

        internal static SuccessorIdentity Get(string careerId)
        {
            SuccessorIdentity value;
            return ByCareer.TryGetValue(careerId ?? String.Empty, out value)
                ? value : null;
        }

        private static Dictionary<string, SuccessorIdentity> Build()
        {
            Dictionary<string, SuccessorIdentity> r =
                new Dictionary<string, SuccessorIdentity>(StringComparer.Ordinal);
            Add(r, "GrailDamsel", "Morgane of the Ashen Veil", "Morgane");
            Add(r, "MinorVampire", "Châtelaine Odile the Sanguine", "Odile");
            Add(r, "WitchHunter", "Interrogator Lukas Brandt", "Lukas");
            Add(r, "Necromancer", "Hierophant Kessel the Unburied", "Kessel");
            Add(r, "Necrarch", "Ossifex Vorago", "Vorago");
            Add(r, "ImperialMagister", "Magister Adelbert Rausch", "Adelbert");
            Add(r, "Spellsinger", "Ilyrien of the Hollow Song", "Ilyrien");
            Add(r, "GreyLord", "Magister Albrecht Mauer", "Albrecht");
            Add(r, "Ironbreaker", "Kadrin Deepwarden", "Kadrin");
            Add(r, "Runelord", "Runesmith Dorek Anvilward", "Dorek");
            Add(r, "OrcShaman", "Wurrzaggit Moon-Screecher", "Wurrzaggit");
            Add(r, "GrailKnight", "Sir Gauvain of the Tarnished Spur", "Gauvain");
            Add(r, "WarriorPriest", "Lector Dieter Falk", "Dieter");
            Add(r, "BloodKnight", "Kastellan Markos the Grim", "Markos");
            Add(r, "Mercenary", "Lieutenant Cosimo Vieri", "Cosimo");
            Add(r, "BlackGrailKnight", "Sir Baudric, Warden of the Black Chalice", "Baudric");
            Add(r, "WarriorPriestUlric", "Wolf-Priest Arnulf Iceblood", "Arnulf");
            Add(r, "Waywatcher", "Selaith Briar-Shadow", "Selaith");
            Add(r, "Warden", "Eldanir of the Spear Glade", "Eldanir");
            Add(r, "KnightOldWorld", "Sir Gerhardt of the Iron Mile", "Gerhardt");
            Add(r, "Slayer", "Dromri Redcrest", "Dromri");
            Add(r, "OrcBoss", "Grukk Bonekruncha", "Grukk");
            return r;
        }

        private static void Add(Dictionary<string, SuccessorIdentity> map,
            string careerId, string fullName, string firstName)
        {
            map.Add(careerId, new SuccessorIdentity
            {
                FullName = fullName,
                FirstName = firstName
            });
        }
    }

    internal sealed class RecognitionDialogue
    {
        internal string Opening;
        internal string SetRecognition;
        internal string VictoryRecognition;
        internal string ReasonToJoin;
        internal string InviteChoice;
        internal string RecruitmentQuestion;
        internal string AcceptanceLine;
        internal string PostponeLine;
    }

    internal static class RecognitionDialogues
    {
        private static readonly Dictionary<string, RecognitionDialogue> ByCareer = Build();

        internal static RecognitionDialogue Get(string careerId)
        {
            RecognitionDialogue value;
            return ByCareer.TryGetValue(careerId ?? String.Empty, out value)
                ? value : null;
        }

        private static Dictionary<string, RecognitionDialogue> Build()
        {
            Dictionary<string, RecognitionDialogue> r =
                new Dictionary<string, RecognitionDialogue>(StringComparer.Ordinal);
            Add(r, "GrailDamsel",
                "The blight around Ysabeau quiets. She studies you as if listening to a voice beyond the ruined chapel.",
                "Every relic of the veiled sisterhood answers you together. Their pattern is whole again, and even their corruption cannot hide the Lady's design.",
                "You crossed my wards and cast me down while bearing that completed mystery. Steel won the hour; the sign revealed what the hour meant.",
                "My visions no longer end at this chapel. They follow your road. I would walk it and learn whether you are restoration, judgement, or both.",
                "Ask what her vision demands", "Then leave this ruin and read the road beside me.",
                "So the omen is accepted. I will bring veil-lore, sight and what grace remains to your company.",
                "The vision does not fade because you hesitate. Return in the full vestments, and we shall speak again.");
            Add(r, "MinorVampire",
                "Aleron wipes a line of dark blood from his mouth and offers a courtly bow sharpened by humiliation.",
                "You wear the five treasures as a single inheritance. Lesser thieves would have displayed them; you have made them obey.",
                "To lose to my own perfected legacy is an exquisite insult—and proof that you are prey no longer.",
                "Your ascent promises rarer contests than this tired sepulchre. Pride counsels vengeance; appetite counsels proximity. Appetite wins.",
                "Offer him a place at your side", "Trade this crypt for my court of war. Will you come willingly?",
                "Willingly, for now. Call me companion; never mistake that word for tame.",
                "Keep your invitation warm. Immortals have patience, and I have not forgotten what you wear.");
            Add(r, "WitchHunter",
                "Krieger keeps one hand near his pistol. Defeat has exhausted him; suspicion has not.",
                "I inspected every piece when they were scattered. Together they form an instrument no dabbler, cultist or fortunate looter could safely command.",
                "You bore the complete panoply through my judgement and prevailed. That does not make you innocent. It makes ignorance an inadequate charge.",
                "The trail around these relics reaches beyond my ruined cell. Better that I watch you closely—and turn my weapons upon the corruption that follows you.",
                "Submit a formal invitation", "Walk with me, Krieger. Judge my enemies and keep judging me.",
                "Agreed. I join your clan as witness and hunter. Give me cause for one role to eclipse the other.",
                "Prudent. Present yourself in the complete panoply when you are prepared for scrutiny again.");
            Add(r, "Necromancer",
                "Mordechai rises with the slow irritation of a corpse refusing its grave.",
                "Five vessels, one grammar of death. You have assembled the set and imposed a living will upon every syllable.",
                "You defeated the hand that taught those relics to hunger. Such an inversion deserves study, not another wasteful corpse-pile.",
                "Your campaigns will furnish battlefields, remains and forbidden thresholds in abundance. I offer knowledge; you offer access to history's freshest graves.",
                "Propose an alliance of convenience", "Leave the barrow and practice your art under my banner.",
                "Under it, beside it—prepositions are for the living. Very well. I will accompany you.",
                "Delay, then. Death keeps accounts longer than kings do, and this offer remains entered.");
            Add(r, "Necrarch",
                "Azrad regards his wounds as disappointing data, then turns his pale attention to your equipment.",
                "The five specimens resonate without destructive interference. You have achieved a stable configuration my servants could only transport in fragments.",
                "Your victory demonstrates repeatable control under battlefield stress. Brute force alone could not have produced that result.",
                "I require mobile laboratories, anomalous foes and a subject capable of surviving my conclusions. Your company satisfies all three conditions.",
                "Invite him to continue his studies", "Join me, and test your conclusions against the wider world.",
                "Accepted. Preserve my workspace and do not confuse cooperation with sentiment.",
                "A defensible delay. Return with all five specimens equipped when your curiosity exceeds your caution.");
            Add(r, "ImperialMagister",
                "Volker steadies his breathing and traces the disciplined conjunctions binding your five relics.",
                "The complete array is no heap of enchanted trophies. You have balanced its channels, controlled its feedback and carried the Colleges' discipline into motion.",
                "You broke my prepared field while sustaining that configuration. The victory proves comprehension under pressure—the only examination that matters here.",
                "My observatory has become a cage for knowledge. Your road offers phenomena worth recording and dangers that require a properly trained magister.",
                "Offer him a place as your magister", "Bring your discipline to my clan, Volker.",
                "I accept. We shall replace improvisation with method—and discover when method must yield to genius.",
                "Then our compact remains unsigned. Return with the full array when you are ready to proceed.");
            Add(r, "Spellsinger",
                "The wounded boughs lean toward Lethariel as her song falls to a whisper.",
                "All five echoes answer in you: root, thorn, wind, memory and the blade-song that binds them. The set is a harmony again.",
                "You overcame me without silencing that harmony. Athel Loren does not grant such accord to a merely skilful despoiler.",
                "The forest's pain travels beyond this waystone. I will follow its echoes beside you, guarding what may yet be healed and cutting away what cannot.",
                "Ask her to carry the song onward", "Walk beyond the bough with me, Lethariel.",
                "I will. Where your path wounds the world, I shall speak; where it defends life, my song is yours.",
                "The forest understands seasons of waiting. Return in the complete harmony when your path is chosen.");
            Add(r, "GreyLord",
                "Veyl's outline seems to occupy two places before shadow settles around the defeated magister.",
                "Five pieces, five apparent truths, one concealed design. You saw through the set's misdirections and made the whole answer to you.",
                "You defeated the position I allowed you to see—and the one behind it. That is rarer than strength and more useful.",
                "The Grey Order serves best where certainty fails. Your road is thick with false faces; I would learn which masks you break and which you choose to wear.",
                "Invite him into your confidence", "Join my clan, Veyl. Bring your shadows with you.",
                "They were already here. Now you may count their master among your companions.",
                "Perhaps refusal is another mask. Wear it until you return with the complete set and a clearer purpose.");
            Add(r, "Ironbreaker",
                "Durgan plants his shield, drags himself upright and measures you in a long Dawi silence.",
                "Every plate and fastening is present. You did not merely gather gromril work; you bore its weight without shame or failure.",
                "You stood through the killing ground and broke my guard while armoured in the full craft. That is proof a Dawi can enter in the ledger.",
                "My duty was to test the vault's claimant. The test is answered. I can better honour hold and oath by making certain the armour is carried against worthy foes.",
                "Ask him to bind his duty to your road", "Take a place in my shieldwall, Ironmantle.",
                "Aye. I give my word before stone and ancestors: while the oath holds, my shield holds with you.",
                "No oath should be hurried. Return in the full harness when your words are ready for the ledger.");
            Add(r, "Runelord",
                "Baragor's gaze moves from each rune to the next, reading your armour like a recovered ancestral tablet.",
                "The sequence is complete. None of the master-runes quarrel, none are carried upside-down, and the old work knows your hand.",
                "You overcame my wards and my hammer while bearing the whole inheritance. The ancestors have rendered a verdict through deed.",
                "These runes should meet the age's greatest perils, not gather soot in a contested vault. I will travel to tend them and add worthy names to their record.",
                "Offer him stewardship of the runes", "Come with me, Embermark. Keep the old craft alive in war.",
                "By forge, anvil and the names before mine, I accept. Treat the runes rightly and you will have my craft.",
                "Wisely weighed. Return with every rune in its place when you are prepared to make an oath of it.");
            Add(r, "OrcShaman",
                "Nazgob's eyes roll toward the moon, then snap back to the crackling relics around you.",
                "All da shiny bitz is shoutin' at once—and dey's shoutin' your name! Gork says dat means you're brutal. Mork says it means you're cunnin'. Might be da uvver way round.",
                "You krumped Nazgob wearin' da whole vision. Dat makes da krumpin' prophecy true, which is dead impressive prophecy-work.",
                "Da Waaagh! round you is loud enough to bend moons. Nazgob wants close seats when it goes bang—and plenty heads to fill with green fire.",
                "Tell him to follow the greater Waaagh!", "Come with me, Moon-Eater. Follow the vision.",
                "Knew you'd say dat! Nazgob's comin'. If your Waaagh! gets quiet, I'll scream till it starts again.",
                "Dat's still part of da vision. Come back in all da bitz when you stop muckin' about.");
            Add(r, "GrailKnight",
                "Malrec kneels upon one mailed knee, his broken pride held straighter than his wounded body.",
                "The five relics rest upon you as a knightly investiture, not plunder. Whatever shadow touched them, you have mastered the whole burden.",
                "You met my charge bearing every piece and unhorsed my claim with honour in open battle. I cannot call that chance without making my own vows worthless.",
                "My old quest ended in corruption and repetition. Yours still moves. Let my lance seek redemption—or a final accounting—upon your road.",
                "Invite him to rise as your companion", "Rise, Sir Malrec. Ride beneath my banner.",
                "I rise by my own strength, yet I ride by earned accord. My lance and oath are joined to your cause.",
                "Then I remain keeper of my own counsel. Return fully arrayed when you would ask again.");
            Add(r, "WarriorPriest",
                "Konrad Voss braces himself upon his hammer and fixes you with a preacher's unsoftened stare.",
                "You bear every consecrated piece, and together they proclaim resolve rather than vanity. A divided testament has become a single armour of conviction.",
                "I tested that conviction with hammer, prayer and blood. You stood, struck true and cast me down. Sigmar grants no worth to excuses after such a trial.",
                "The Empire's enemies multiply beyond this procession. Your victories can become righteous purpose if a lector stands near enough to demand it.",
                "Call him to a wider crusade", "Join my clan, Voss. Let our next trial face Sigmar's enemies.",
                "Then witness my oath: my hammer serves the righteous work we undertake, and my voice will name cowardice wherever it hides.",
                "Reflection before an oath is no sin. Return in the full vestments when resolve has answered doubt.");
            Add(r, "BloodKnight",
                "Varos rises with a predator's grace, smiling at the first defeat he has respected in years.",
                "You have assembled the crimson panoply and awakened its full martial promise. It does not adorn you; it announces you.",
                "You faced my charge in that complete harness and won. There is no lineage, title or excuse left to place between us—only the result.",
                "A warrior who can defeat me can lead me toward opponents worth the centuries. I would rather hunt beside such strength than squander eternity retesting it here.",
                "Offer him greater battles", "Ride with me, Varos. Seek worthy blood beneath my banner.",
                "Gladly. Feed me battles that deserve remembrance, and you will never question my place in the charge.",
                "Anticipation sharpens the next contest. Return in the full panoply when you want my answer made final.");
            Add(r, "Mercenary",
                "Luccio checks the edge of his blade, finds it intact, and gives you the rueful nod of a captain revising his accounts.",
                "Five company treasures, recovered and fielded together. You have done what quartermasters, thieves and claimants failed to manage.",
                "Then you beat the Black Company while carrying its whole legend on your back. Reputation has changed hands more cleanly than any pay chest.",
                "A captain knows when the profitable future has moved to another banner. I bring discipline, contacts and a sword; you bring campaigns worth surviving.",
                "Offer him a captain's place", "Name your terms, Ferrante. Join my clan.",
                "Terms accepted: fair spoils, hard targets, no wasted lives. You have my contract and my professional loyalty.",
                "No competent captain signs while the ink is shaking. Return in the complete kit when you want to close the bargain.");
            Add(r, "BlackGrailKnight",
                "Sir Severin bows over his darkened blade with the grave ceremony of a court that should have died long ago.",
                "The Black Grail's five honours are united upon you. You wear damnation with discipline; the relics have found neither victim nor fool.",
                "You defeated their Keeper beneath the full weight of that inheritance. By the law of broken chivalry, your claim now stands above mine.",
                "Mousillon taught me that vows survive purity. I will bind mine to the stronger quest and see what kingdom your victories carve from the night.",
                "Accept his dark fealty", "Keeper, leave the procession. Ride with me.",
                "I accept the higher claim. Until death remembers us, the Black Grail rides in your company.",
                "A dark vow ripens in silence. Return fully invested when you choose to speak it.");
            Add(r, "WarriorPriestUlric",
                "Hagen Wolfsbane breathes steam into the cold air and laughs once, harsh and approving.",
                "Every winter-forged relic is on you. Worn together, they carry the bite of the north and the honesty of exposed steel.",
                "You endured my fury, answered it without flinching and left me in the snow. Ulric measures courage in deeds; the measure is plain.",
                "A stronger hunt runs with your pack. I will follow it, test those who boast too loudly, and bring the White Wolf's wrath to foes worthy of winter.",
                "Invite him into the pack", "Run with my company, Wolfsbane.",
                "Aye. Your road smells of storm and worthy blood. My hammer joins the pack.",
                "A wolf circles before choosing the trail. Return in the full winter harness when you call again.");
            Add(r, "Waywatcher",
                "Aelir watches from one knee, expression unreadable beneath blood and leaf-shadow.",
                "All five tokens are where they belong. No clatter. No boast. The set moves with you as the forest intended.",
                "You found every false trail, survived the killing ground and defeated me while wearing the whole craft. I misjudged your tread.",
                "Threats cross the forest's borders and return wearing new skins. Following you offers clearer shots at the hands guiding them.",
                "Offer a place without demanding trust", "Walk my road, Thorn-Eyed. Choose your own shadows beside us.",
                "I will walk it. If your road turns against the forest, you will hear my answer only after the arrow lands.",
                "Good. Distrust kept sharp is useful. Return in the complete set when the road calls again.");
            Add(r, "Warden",
                "Caerwyn plants his spear in the earth. Behind him, the Hunted fall silent while he studies every recovered piece in your keeping.",
                "The warden's charge is whole upon you. Each piece guards the others, as roots bind soil and shieldwood binds a line. These were never trophies taken from us; they were the inheritance of the spear-line condemned beside me.",
                "When Orion's horn called, I held a threatened ward-line instead of joining the Hunt. The glade lived, and the forest named my choice defiance. Now you have broken my defence while bearing the whole charge without profaning it. I cannot dismiss that as theft or chance.",
                "I have spent too long mistaking exile for duty. A warden's oath belongs to what must be defended, not only to the ground where judgment fell. Your road reaches threats before they reach another glade; there may be a truer watch there than in endless flight from the Hunt.",
                "Ask him to extend his watch", "Guard the wider road with me, Caerwyn.",
                "Then my watch moves with yours. I will hold the line wherever your cause gives the innocent ground to stand upon. Let the Wild Hunt judge the deed when it catches us.",
                "Duty permits deliberation. Return fully armed as a warden when you would renew the offer.");
            Add(r, "KnightOldWorld",
                "Eckhardt removes his helm and regards you with the severe courtesy of an older martial order.",
                "You have restored the five heirlooms to one fighting harness. Their legacy lives in use, formation and discipline—not in a reliquary.",
                "You defeated their custodian while bearing the completed inheritance. By every honest custom of the road, you have earned succession to its burden.",
                "The Old World's roads darken, and no worthy knight preserves tradition by guarding yesterday. I would carry its standard into your battles.",
                "Offer him a place in your order", "Ride with me, Sir Eckhardt. Let the old oath face new wars.",
                "With honour. I join your clan as knight and companion, answerable to the standard we make through deeds.",
                "A knight should know the road before swearing to it. Return in the full harness when you are certain.");
            Add(r, "Slayer",
                "Kragni Oathscar spits blood, sees that he still lives, and scowls more fiercely at survival than defeat.",
                "You carry every relic of the oath-host. Together they speak of foes faced without shield, excuse or retreat.",
                "You bested me while wearing the whole tale, yet denied me the doom I sought. That makes you either cursed—or a road to something greater.",
                "I swear no service. My Slayer oath remains my only master. But the greatest monsters seem to gather in your wake, and I will walk there to meet my worthy death.",
                "Offer him the road to greater foes", "Travel with us, Oathscar. Seek your doom where our enemies stand thickest.",
                "Aye. Companion, then—not servant. Lead toward beasts fit to end the shame, and keep out of my axe's way.",
                "The doom can wait a little longer; it always has. Return in the full oath-gear when you have greater foes to promise.");
            Add(r, "OrcBoss",
                "Morglug hauls himself up, glares at the watching boyz and bares his tusks in reluctant calculation.",
                "You got all five boss-bitz on, and none of 'em fell off when da hittin' started. Dey know who's strongest now.",
                "You krumped Morglug proper while wearin' da lot. Any git sayin' dat don't make you boss can argue with your choppa—and mine.",
                "Big fights follow big bosses. Morglug'll join your mob, smash whoever you point at, and keep an eye out in case you stop bein' strongest.",
                "Claim his strength for your Waaagh!", "Fall in, Ironjaw. You're fighting with my mob now.",
                "Yeah, yeah—you's boss today. Morglug's comin', and da next lot gets krumped twice as hard.",
                "Fine. Go think. Come back wearin' all da boss-bitz when you remember who's toughest.");
            return r;
        }

        private static void Add(Dictionary<string, RecognitionDialogue> map,
            string careerId, string opening, string setRecognition,
            string victoryRecognition, string reasonToJoin, string inviteChoice,
            string recruitmentQuestion, string acceptanceLine,
            string postponeLine)
        {
            map.Add(careerId, new RecognitionDialogue
            {
                Opening = opening,
                SetRecognition = setRecognition,
                VictoryRecognition = victoryRecognition,
                ReasonToJoin = reasonToJoin,
                InviteChoice = inviteChoice,
                RecruitmentQuestion = recruitmentQuestion,
                AcceptanceLine = acceptanceLine,
                PostponeLine = postponeLine
            });
        }
    }
}
