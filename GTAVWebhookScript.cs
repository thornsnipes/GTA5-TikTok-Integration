using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTAVWebhook;
using GTAVWebhook.Types;

public class GTAVWebhookScript : Script
{
    private HttpServer httpServer = new HttpServer();
    private bool isFirstTick = true;
    private readonly List<Vehicle> spawnedVehicles = new List<Vehicle>();
    private readonly List<Attacker> npcList = new List<Attacker>();
    private static readonly Random Rng = new Random();

    public GTAVWebhookScript()
    {
        Tick += OnTick;
        KeyUp += OnKeyUp;
        KeyDown += OnKeyDown;
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (isFirstTick)
        {
            isFirstTick = false;

            Logger.Clear();

            try
            {
                httpServer.Start();
                Logger.Log("HttpServer listening on port " + httpServer.Port);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to start HttpServer - " + ex.Message);
            }
        }

        // keep attacker list in check
        while (npcList.Count > 100)
        {
            try
            {
                npcList[0].Remove();
                npcList.RemoveAt(0);
                Logger.Log("Attacker over limit removed");
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to remove old attacker - " + ex.Message);
            }
        }

        // draw attacker names
        try
        {
            foreach (Attacker attacker in npcList)
                attacker.DrawName();
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to draw attacker names - " + ex.Message);
        }

        // process webhook commands
        CommandInfo command = httpServer.DequeueCommand();
        if (command != null)
        {
            try
            {
                ProcessCommand(command);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to execute command {command.cmd}. Error: {ex.Message}");
            }
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e) { }
    private void OnKeyUp(object sender, KeyEventArgs e) { }

    private void ProcessCommand(CommandInfo command)
    {
        switch (command.cmd)
        {
            // ==== existing commands from your base script ====

            case "kill":
            {
                Game.Player.Character.Kill();
                break;
            }

            case "spawn_vehicle":
            {
                if (Enum.TryParse<VehicleHash>(command.custom, out var vehicleHash))
                {
                    var pos = Game.Player.Character.Position + Game.Player.Character.ForwardVector * 5f;
                    var veh = World.CreateVehicle(new Model(vehicleHash), pos);
                    if (veh != null)
                    {
                        spawnedVehicles.Add(veh);
                        Logger.Log("Vehicle spawned: " + command.custom);
                    }
                }
                else
                {
                    Logger.Log("Cannot parse vehicle name: " + command.custom);
                }
                break;
            }

            case "remove_spawned_vehicles":
            {
                try
                {
                    while (spawnedVehicles.Count > 0)
                    {
                        Logger.Log("Removing vehicle: " + spawnedVehicles[0].DisplayName);
                        spawnedVehicles[0].Delete();
                        spawnedVehicles.RemoveAt(0);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to remove spawned vehicles: " + ex.Message);
                }
                break;
            }

            case "repair_current_vehicle":
            {
                var veh = Game.Player.Character.CurrentVehicle;
                if (veh != null)
                {
                    veh.HealthFloat = veh.MaxHealthFloat;
                    veh.EngineHealth = veh.MaxHealth;
                    Logger.Log("CurrentVehicle Health restored to " + veh.HealthFloat);
                }
                else Logger.Log("Cannot repair current vehicle because player not in vehicle");
                break;
            }

            case "explode_vehicle":
            {
                foreach (Vehicle vehicle in World.GetNearbyVehicles(Game.Player.Character, 20f))
                    vehicle.Explode();
                break;
            }

            case "give_weapon":
            {
                if (Enum.TryParse<WeaponHash>(command.custom, out var weaponHash))
                {
                    Game.Player.Character.Weapons.Give(weaponHash, 9999, true, true);
                    Logger.Log("Weapon given: " + command.custom);
                }
                else Logger.Log("Cannot parse weapon name: " + command.custom);
                break;
            }

            case "set_max_weapon_ammo":
            {
                if (!int.TryParse(command.custom, out int ammo))
                    ammo = 9999;

                if (Game.Player.Character.Weapons.Current != null &&
                    ammo > Game.Player.Character.Weapons.Current.MaxAmmo)
                    ammo = Game.Player.Character.Weapons.Current.MaxAmmo;

                if (Game.Player.Character.Weapons.Current != null)
                    Game.Player.Character.Weapons.Current.Ammo = ammo;
                break;
            }

            case "set_time":
            {
                if (TimeSpan.TryParse(command.custom + ":00", out var ts))
                {
                    World.CurrentTimeOfDay = ts;
                    Logger.Log("Time set to: " + command.custom);
                }
                else Logger.Log("Cannot parse TimeSpan: " + command.custom);
                break;
            }

            case "set_weather":
            {
                if (Enum.TryParse<Weather>(command.custom, out var weather))
                    World.Weather = weather;
                else
                    Logger.Log("Cannot parse Weather: " + command.custom);
                break;
            }

            case "increase_wanted":
            {
                if (Game.Player.WantedLevel < 5) Game.Player.WantedLevel += 1;
                break;
            }

            case "decrease_wanted":
            {
                if (Game.Player.WantedLevel > 0) Game.Player.WantedLevel -= 1;
                break;
            }

            case "max_wanted":
            {
                Game.Player.WantedLevel = 5;
                break;
            }

            case "add_money":
            {
                if (!int.TryParse(command.custom, out int moneyToAdd))
                {
                    Logger.Log("add_money needs a numeric value!");
                    break;
                }

                Game.Player.Money += moneyToAdd;
                if (Game.Player.Money < 0) Game.Player.Money = 0;

                Logger.Log("Player Money set to " + Game.Player.Money);
                break;
            }

            case "set_money":
            {
                if (!int.TryParse(command.custom, out int moneyToSet))
                {
                    Logger.Log("set_money needs a numeric value!");
                    break;
                }

                if (moneyToSet < 0) moneyToSet = 0;
                Game.Player.Money = moneyToSet;

                Logger.Log("Player Money set to " + Game.Player.Money);
                break;
            }

            case "spawn_attackers":
            {
                if (Game.Player.Character.IsInAir)
                {
                    Logger.Log("Cannot spawn attacker because Player IsInAir");
                    break;
                }

                if (!int.TryParse(command.custom, out int num)) num = 1;
                if (num > 50) num = 50;

                for (int i = 0; i < num; i++)
                {
                    Logger.Log("Spawn Attacker");
                    var npc = new Attacker(command.username, false);
                    npcList.Add(npc);
                }
                break;
            }

            case "spawn_attackers_and_shoot":
            {
                if (Game.Player.Character.IsInAir)
                {
                    Logger.Log("Cannot spawn attacker because Player IsInAir");
                    break;
                }

                if (!int.TryParse(command.custom, out int num2)) num2 = 1;
                if (num2 > 50) num2 = 50;

                for (int i = 0; i < num2; i++)
                {
                    Logger.Log("Spawn Attacker with gun");
                    var npc = new Attacker(command.username, true);
                    npcList.Add(npc);
                }
                break;
            }

            case "attackers_start_shooting":
            {
                if (!int.TryParse(command.custom, out int duration)) duration = 30;
                if (duration < 1) duration = 30;

                foreach (Attacker attacker in npcList)
                {
                    Logger.Log("Attacker start shooting");
                    attacker.StartShooting(duration);
                }
                break;
            }

            case "remove_attackers":
            {
                if (!int.TryParse(command.custom, out int num)) num = 1;

                for (int i = 0; i < num; i++)
                {
                    if (npcList.Count > 0)
                    {
                        npcList[^1].Remove();
                        npcList.RemoveAt(npcList.Count - 1);
                        Logger.Log("Attacker removed, remaining: " + npcList.Count);
                    }
                }
                break;
            }

            case "leave_car":
            {
                if (Game.Player.Character.IsInVehicle())
                    Game.Player.Character.Task.LeaveVehicle(LeaveVehicleFlags.None);
                break;
            }

            case "skydive": // <-- included again
            {
                var p = Game.Player.Character;
                p.Position = new Vector3(p.Position.X, p.Position.Y, p.Position.Z + 400f);
                p.Task.Skydive();
                Logger.Log("Skydive started");
                break;
            }

            case "increase_health":
            {
                if (!int.TryParse(command.custom, out int healStep))
                    healStep = 20;

                float newHealthScore = Game.Player.Character.HealthFloat + healStep;
                if (newHealthScore < 0) newHealthScore = 0;
                if (newHealthScore > Game.Player.Character.MaxHealthFloat)
                    newHealthScore = Game.Player.Character.MaxHealthFloat;

                Game.Player.Character.HealthFloat = newHealthScore;
                Logger.Log("Health set to " + Game.Player.Character.HealthFloat);
                break;
            }

            // ==== NEW features mapped from your grid ====

            case "kick_flip":
            {
                var veh = Game.Player.Character.CurrentVehicle;
                if (veh != null)
                {
                    // quick pop + flip
                    veh.Velocity += new Vector3(0f, 0f, 12f);
                    veh.AngularVelocity += new Vector3(5f, 0f, 0f);
                    Logger.Log("Vehicle kick-flipped");
                }
                break;
            }

            case "change_car":
            {
                var player = Game.Player.Character;
                if (player.CurrentVehicle != null)
                    player.CurrentVehicle.Delete();

                var randomHash = GetRandomEnumValue<VehicleHash>();
                var spawnPos = player.Position + player.ForwardVector * 5f;
                var newVeh = World.CreateVehicle(new Model(randomHash), spawnPos);
                if (newVeh != null)
                {
                    newVeh.PlaceOnGround();
                    player.SetIntoVehicle(newVeh, VehicleSeat.Driver);
                    Logger.Log($"Changed to random vehicle: {randomHash}");
                }
                break;
            }

            case "broken_car":
            {
                var veh = Game.Player.Character.CurrentVehicle;
                if (veh != null)
                {
                    veh.EngineHealth = -4000f;
                    veh.PetrolTankHealth = 0f;
                    veh.BodyHealth = 0f;
                    veh.Health = 200;
                    Logger.Log("Current vehicle severely damaged");
                }
                break;
            }

            case "remove_car":
            {
                var veh = Game.Player.Character.CurrentVehicle;
                if (veh != null)
                {
                    veh.Delete();
                    Logger.Log("Removed current vehicle");
                }
                break;
            }

            case "spawn_tank":
            {
                var spawnPos = Game.Player.Character.Position + Game.Player.Character.ForwardVector * 10f;
                var tank = World.CreateVehicle(VehicleHash.Rhino, spawnPos);
                if (tank != null)
                {
                    spawnedVehicles.Add(tank);
                    Logger.Log("Rhino tank spawned");
                }
                break;
            }

            case "towed":
            {
                var veh = Game.Player.Character.CurrentVehicle;
                if (veh != null)
                {
                    veh.Delete();
                    Logger.Log("Vehicle towed away!");
                }
                break;
            }

            case "5_balla_squad":
            {
                SpawnHostiles(PedHash.BallaEast01GMY, 5, giveGun: true);
                Logger.Log("Spawned 5 Ballas");
                break;
            }

            case "5_aliens":
            {
                // Movie alien ped
                SpawnHostiles(PedHash.MovAlien01, 5, giveGun: true);
                Logger.Log("Spawned 5 Aliens");
                break;
            }

            case "5_chimp":
            {
                SpawnHostiles(PedHash.Chimp, 5, giveGun: false);
                Logger.Log("Spawned 5 Chimps");
                break;
            }

            case "5_juggernauts":
            {
                var peds = SpawnHostiles(PedHash.Juggernaut01M, 5, giveGun: true, weapon: WeaponHash.HeavyShotgun);
                foreach (var ped in peds)
                {
                    ped.Armor = 500;
                    ped.Health = ped.MaxHealth;
                }
                Logger.Log("Spawned 5 Juggernauts");
                break;
            }

            case "random_teleport":
            {
                var player = Game.Player.Character;
                // pick an offset and ask the game for a nearby road/ground
                var offset = new Vector3(RandRange(-1500f, 1500f), RandRange(-1500f, 1500f), 0f);
                var tryPos = player.Position + offset;
                var safe = World.GetNextPositionOnStreet(tryPos);
                if (safe == Vector3.Zero)
                {
                    float groundZ = World.GetGroundHeight(tryPos);
                    safe = new Vector3(tryPos.X, tryPos.Y, groundZ + 2f);
                }
                player.Position = safe;
                Logger.Log("Random teleport complete");
                break;
            }

            case "throw":
            {
                var p = Game.Player.Character;
                var dir = p.ForwardVector;
                p.ApplyForce(dir * 25f + new Vector3(0f, 0f, 5f));
                Logger.Log("Player thrown forward");
                break;
            }

            case "hospital":
            {
                // Central LS hospital rooftop entrance area
                var hospital = new Vector3(298.66f, -584.74f, 43.26f);
                var p = Game.Player.Character;
                p.Position = hospital;
                p.Health = p.MaxHealth;
                Logger.Log("Teleported to hospital and healed");
                break;
            }

            case "rocketman":
            {
                var p = Game.Player.Character;
                p.Velocity = new Vector3(0f, 0f, 60f);
                Logger.Log("Rocketman launch!");
                break;
            }

            case "magnetman":
            {
                var player = Game.Player.Character;
                var nearby = World.GetNearbyVehicles(player, 40f);
                foreach (var v in nearby)
                {
                    if (v == player.CurrentVehicle) continue;
                    Vector3 dir = (player.Position - v.Position);
                    float dist = Math.Max(dir.Length(), 1f);
                    dir /= dist; // normalize
                    v.ApplyForce(dir * 80f);
                }
                Logger.Log("Pulled nearby vehicles toward player");
                break;
            }

            case "end_world":
            {
                // drop under the map a bit
                Game.Player.Character.Position = new Vector3(0f, 0f, -50f);
                Logger.Log("End World: dropped below map");
                break;
            }

            case "back_to_airport":
            {
                var airport = new Vector3(-1034.6f, -2737.6f, 13.8f);
                Game.Player.Character.Position = airport;
                Logger.Log("Back to LSIA");
                break;
            }

            default:
            {
                Logger.Log("Unknown Command " + command.cmd);
                break;
            }
        }
    }

    // ---- helpers ----

    private static float RandRange(float min, float max)
    {
        return (float)(min + Rng.NextDouble() * (max - min));
    }

    private static TEnum GetRandomEnumValue<TEnum>()
    {
        Array values = Enum.GetValues(typeof(TEnum));
        return (TEnum)values.GetValue(Rng.Next(values.Length));
    }

    /// <summary>
    /// Spawns hostile peds near player and orders them to attack.
    /// </summary>
    private static List<Ped> SpawnHostiles(PedHash model, int count, bool giveGun, WeaponHash weapon = WeaponHash.Pistol)
    {
        var list = new List<Ped>();
        var player = Game.Player.Character;

        for (int i = 0; i < count; i++)
        {
            Vector3 spawn =
                player.Position +
                player.ForwardVector * RandRange(3f, 6f) +
                player.RightVector * RandRange(-4f, 4f);

            var ped = World.CreatePed(model, spawn);
            if (ped == null) continue;

            if (giveGun)
                ped.Weapons.Give(weapon, 250, true, true);

            ped.Task.FightAgainst(player);
            list.Add(ped);
        }

        return list;
    }
}
