using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using LobbyClient;
using ZeroKWeb;
using ZeroKWeb.Controllers;
using ZeroKWeb.SpringieInterface;
using ZkData;

public static class PlanetWarsTurnHandler {

    /// <summary>
    /// Process planet wars turn
    /// </summary>
    /// <param name="mapName"></param>
    /// <param name="extraData"></param>
    /// <param name="db"></param>
    /// <param name="winNum">0 = attacker wins, 1 = defender wins</param>
    /// <param name="players"></param>
    /// <param name="text"></param>
    /// <param name="sb"></param>
    public static void EndTurn(string mapName, List<string> extraData, ZkDataContext db, int? winNum, List<Account> players, StringBuilder text, SpringBattle sb, List<Account> attackers)
    {
        if (extraData == null) extraData = new List<string>();
        Galaxy gal = db.Galaxies.Single(x => x.IsDefault);
        Planet planet = gal.Planets.Single(x => x.Resource.InternalName == mapName);

        text.AppendFormat("Battle on http://zero-k.info/PlanetWars/Planet/{0} has ended\n", planet.PlanetID);


        Faction attacker = attackers.Where(x => x.Faction != null).Select(x => x.Faction).First();
        var defenders = players.Where(x => x.FactionID != attacker.FactionID && x.FactionID != null).ToList();

        if (attacker == null)
        {
            text.AppendLine("ERROR: Invalid attacker");
            return;
        }

        Faction winnerFaction = null;
        bool wasWinerCcDestroyed = false;

        if (winNum != null)
        {
            if (winNum == 0) winnerFaction = attacker;
            else winnerFaction = planet.Faction;
            wasWinerCcDestroyed = extraData.Any(x => x.StartsWith("hqkilled," + winNum));
        }
        
        int dropshipsSent = (planet.PlanetFactions.Where(x => x.Faction == attacker).Sum(x => (int?)x.Dropships) ?? 0);
        string influenceReport = "";

        // distribute influence
        if (winnerFaction != null)
        {
            // save influence gains
            if (winnerFaction == attacker)
            {
                // give influence to main attackers
                double planetDropshipDefs = (planet.PlanetStructures.Where(x => x.IsActive).Sum(x => x.StructureType.EffectDropshipDefense) ?? 0);
                double planetIpDefs = (planet.PlanetStructures.Where(x => x.IsActive).Sum(x => x.StructureType.EffectReduceBattleInfluenceGain) ?? 0);

                double baseInfluence = GlobalConst.BaseInfluencePerBattle;
                double influence = baseInfluence;

                double shipBonus = winnerFaction == attacker ? (dropshipsSent - planetDropshipDefs) * GlobalConst.InfluencePerShip : 0;
                double defenseBonus = winnerFaction == attacker ? -planetIpDefs : 0;
                double techBonus = winnerFaction.GetFactionUnlocks().Count() * GlobalConst.InfluencePerTech;
                double ccMalus = wasWinerCcDestroyed ? -(influence + shipBonus + techBonus) * GlobalConst.InfluenceCcKilledAttackerMultiplier : 0;

                influence = influence + shipBonus + techBonus + ccMalus + defenseBonus;
                if (influence < 0) influence = 0;
                influence = Math.Floor(influence * 100) / 100;

                // main winner influence 
                PlanetFaction entry = planet.PlanetFactions.FirstOrDefault(x => x.Faction == winnerFaction);
                if (entry == null)
                {
                    entry = new PlanetFaction { Faction = winnerFaction, Planet = planet, };
                    planet.PlanetFactions.Add(entry);
                }

                entry.Influence += influence;


                // clamping of influence
                // gained over 100, sole owner
                if (entry.Influence >= 100)
                {
                    entry.Influence = 100;
                    foreach (var pf in planet.PlanetFactions.Where(x => x.Faction != winnerFaction)) pf.Influence = 0;
                }
                else
                {
                    var sumOthers = planet.PlanetFactions.Where(x => x.Faction != winnerFaction).Sum(x => (double?)x.Influence) ?? 0;
                    if (sumOthers + entry.Influence > 100)
                    {
                        var excess = sumOthers + entry.Influence - 100;
                        foreach (var pf in planet.PlanetFactions.Where(x => x.Faction != winnerFaction)) pf.Influence -= pf.Influence / sumOthers * excess;
                    }
                }
                try
                {
                    influenceReport = String.Format("{0} gained {1} influence ({2}{3}{4}{5}{6})",
                        winnerFaction.Shortcut,
                        influence,
                        baseInfluence + " base",
                        defenseBonus != 0 ? " -" + -defenseBonus + " from defenses" : "",
                        techBonus > 0 ? " +" + techBonus + " from techs" : "",
                        shipBonus > 0 ? " +" + shipBonus + " from ships" : "",
                        ccMalus != 0 ? " " + ccMalus + " from destroyed CC" : "");
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                }
            }
        }

        // distribute metal
        var attackersTotalMetal = Math.Floor(GlobalConst.PlanetWarsAttackerMetal);
        var defendersTotalMetal = Math.Floor(((winnerFaction != attacker && winnerFaction != null) ? GlobalConst.PlanetWarsDefenderWinMetal: GlobalConst.PlanetWarsDefenderLoseMetal)  * (wasWinerCcDestroyed ? GlobalConst.CcDestroyedMetalMultDefenders : 1.0));
        var attackerMetal = Math.Floor(attackersTotalMetal/attackers.Count);
        var defenderMetal = Math.Floor(defendersTotalMetal / defenders.Count);
        foreach (Account w in attackers)
        {
            w.ProduceMetal(attackerMetal);
                var ev = Global.CreateEvent("{0} gained {1} metal from battle {2}",
                                            w,
                                            attackerMetal,
                                            sb);
                db.Events.InsertOnSubmit(ev);
                text.AppendLine(ev.PlainText);
        }
        
        foreach (Account w in defenders)
        {
            w.ProduceMetal(defenderMetal);
            var ev = Global.CreateEvent("{0} gained {1} metal from battle {2}",
                                        w,
                                        defenderMetal,
                                        sb);

            db.Events.InsertOnSubmit(ev);
            text.AppendLine(ev.PlainText);
        }


        // remove dropships
        foreach (var pf in planet.PlanetFactions.Where(x => x.Faction == attacker)) pf.Dropships = 0;


        // add attack points
        foreach (Account acc in players)
        {
            int ap = acc.Faction == winnerFaction ? GlobalConst.AttackPointsForVictory : GlobalConst.AttackPointsForDefeat;
            if (acc.Faction != null)
            {
                AccountPlanet entry = planet.AccountPlanets.SingleOrDefault(x => x.AccountID == acc.AccountID);
                if (entry == null)
                {
                    entry = new AccountPlanet { AccountID = acc.AccountID, PlanetID = planet.PlanetID };
                    db.AccountPlanets.InsertOnSubmit(entry);
                }

                entry.AttackPoints += ap;
            }
            acc.PwAttackPoints += ap;
        }

        // paranoia!
        try
        {
            string metalStringWinner = String.Format("Attackers gained {0} metal.", attackersTotalMetal);
            string metalStringLoser = defenders.Any() ? String.Format("Defenders gained {0} metal{1}. ", defendersTotalMetal, wasWinerCcDestroyed ? String.Format(" ({0:F0}% because CC was destroyed)", GlobalConst.CcDestroyedMetalMultDefenders * 100) : "") : "";
            var mainEvent = Global.CreateEvent("{0} attacked {7} {1} with {2} dropships in {3} and {4}{5}{6}",
                attacker,
                planet,
                dropshipsSent,
                sb,
                winnerFaction == attacker ? "won. " + influenceReport + ". " : "lost. ",
                metalStringWinner,
                metalStringLoser,
                planet.Faction
                );
            db.Events.InsertOnSubmit(mainEvent);
            text.AppendLine(mainEvent.PlainText);

        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
        }

        // destroy pw structures killed ingame
        if (winnerFaction != attacker)
        {
            var handled = new List<string>();
            foreach (string line in extraData.Where(x => x.StartsWith("structurekilled")))
            {
                string[] data = line.Substring(16).Split(',');
                string unitName = data[0];
                if (handled.Contains(unitName)) continue;
                handled.Add(unitName);
                foreach (PlanetStructure s in planet.PlanetStructures.Where(x => x.StructureType.IngameUnitName == unitName))
                {
                    if (s.StructureType.IsIngameDestructible)
                    {
                        s.IsActive = false;
                        s.ActivatedOnTurn = gal.Turn + (int)(s.StructureType.TurnsToActivate * (GlobalConst.StructureIngameDisableTimeMult-1));

                        var ev = Global.CreateEvent("{0} has been disabled on {1} planet {2}. {3}", s.StructureType.Name, planet.Faction , planet, sb);
                        db.Events.InsertOnSubmit(ev);
                        text.AppendLine(ev.PlainText);
                    }
                }
            }
        }
        else
        {
            // attacker won disable all
            foreach (var s in planet.PlanetStructures.Where(x => x.StructureType.IsIngameDestructible))
            {
                s.IsActive = false;
                s.ActivatedOnTurn = gal.Turn + (int)(s.StructureType.TurnsToActivate * (GlobalConst.StructureIngameDisableTimeMult-1));
            }
            // destroy structures by battle (usually defenses)
            foreach (PlanetStructure s in planet.PlanetStructures.Where(x => x.StructureType.BattleDeletesThis).ToList()) planet.PlanetStructures.Remove(s);

            var ev = Global.CreateEvent("All structures have been disabled on {0} planet {1}. {2}", planet.Faction, planet, sb);
            db.Events.InsertOnSubmit(ev);
            text.AppendLine(ev.PlainText);
        }

        db.SubmitAndMergeChanges();

        gal.DecayInfluence();
        gal.SpreadInfluence();

        // process faction energies
        foreach (var fac in db.Factions.Where(x => !x.IsDeleted)) fac.ProcessEnergy(gal.Turn);

        // process production
        gal.ProcessProduction();


        // process treaties
        foreach (var tr in db.FactionTreaties.Where(x => x.TreatyState == TreatyState.Accepted || x.TreatyState == TreatyState.Suspended))
        {
            if (tr.ProcessTrade(false))
            {
                tr.TreatyState = TreatyState.Accepted;
                if (tr.TurnsTotal != null)
                {
                    tr.TurnsRemaining--;
                    if (tr.TurnsRemaining <= 0)
                    {
                        tr.TreatyState = TreatyState.Invalid;
                        db.FactionTreaties.DeleteOnSubmit(tr);
                    }
                }
            }
            else tr.TreatyState = TreatyState.Suspended;
        }

        // burn extra energy
        foreach (var fac in db.Factions.Where(x => !x.IsDeleted)) fac.ConvertExcessEnergyToMetal();


        int? oldOwner = planet.OwnerAccountID;
        gal.Turn++;
        db.SubmitAndMergeChanges();

        db = new ZkDataContext(); // is this needed - attempt to fix setplanetownersbeing buggy
        PlanetwarsController.SetPlanetOwners(db, sb);
        gal = db.Galaxies.Single(x => x.IsDefault);

        planet = gal.Planets.Single(x => x.Resource.InternalName == mapName);
        if (planet.OwnerAccountID != oldOwner && planet.OwnerAccountID != null)
        {
            text.AppendFormat("Congratulations!! Planet {0} was conquered by {1} !!  http://zero-k.info/PlanetWars/Planet/{2}\n",
                planet.Name,
                planet.Account.Name,
                planet.PlanetID);
        }

        try
        {

            // store history
            foreach (Planet p in gal.Planets)
            {
                db.PlanetOwnerHistories.InsertOnSubmit(new PlanetOwnerHistory
                {
                    PlanetID = p.PlanetID,
                    OwnerAccountID = p.OwnerAccountID,
                    OwnerClanID = p.OwnerAccountID != null ? p.Account.ClanID : null,
                    OwnerFactionID = p.OwnerFactionID,
                    Turn = gal.Turn
                });
            }

            db.SubmitChanges();
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
            text.AppendLine("error saving history: " + ex);
        }

        //rotate map
        if (GlobalConst.RotatePWMaps)
        {
            db = new ZkDataContext();
            gal = db.Galaxies.Single(x => x.IsDefault);
            planet = gal.Planets.Single(x => x.Resource.InternalName == mapName);
            var mapList = db.Resources.Where(x => x.MapPlanetWarsIcon != null && x.Planets.Where(p => p.GalaxyID == gal.GalaxyID).Count() == 0 && x.FeaturedOrder != null
                                                  && x.ResourceID != planet.MapResourceID && x.MapWaterLevel == planet.Resource.MapWaterLevel).ToList();
            if (mapList.Count > 0)
            {
                int r = new Random().Next(mapList.Count);
                int resourceID = mapList[r].ResourceID;
                Resource newMap = db.Resources.Single(x => x.ResourceID == resourceID);
                text.AppendLine(String.Format("Map cycler - {0} maps found, selected map {1} to replace map {2}", mapList.Count, newMap.InternalName, planet.Resource.InternalName));
                planet.Resource = newMap;
                gal.IsDirty = true;
            }
            else
            {
                text.AppendLine("Map cycler - no maps found");
            }
            db.SubmitAndMergeChanges();
        }
    }
}