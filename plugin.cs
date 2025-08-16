using BepInEx;
using BepInEx.Unity.IL2CPP;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using System.Text;
using System.Text.Json;
using Unity.Entities;
using VampireCommandFramework;


// Wrap everything in a normal namespace block rather than a file‑scoped namespace
namespace NazarickAura
{

    /// <summary>
    ///     Provides access to the server world, EntityManager and systems
    ///     without relying on the outdated Bloodstone framework. This class
    ///     locates the Unity.Entities.World named "Server" and caches
    ///     frequently used objects. Inspired by the Core class of
    ///     KindredCommands, it enumerates World.s_AllWorlds to find the
    ///     correct world. If the world cannot be found the plugin will
    ///     emit an error to the log and functionality will be disabled.
    /// </summary>
    internal static class ServerContext
    {
        public static World? World { get; private set; }
        public static EntityManager EntityManager { get; private set; }
        public static DebugEventsSystem DebugEventsSystem { get; private set; }
        private static BepInEx.Logging.ManualLogSource? _log;

        /// <summary>
        ///     Must be called during plugin Load() to initialize the server
        ///     context. It finds the world named "Server" using the same
        ///     logic as KindredCommands.Core.GetWorld and then caches the
        ///     EntityManager and DebugEventsSystem. If initialization fails
        ///     a warning is logged.
        /// </summary>
        public static void Initialize(BepInEx.Logging.ManualLogSource logger)
        {
            _log = logger;
            World = FindWorldByName("Server");
            if (World == null)
            {
                _log?.LogError("NazarickAura: Could not locate the Server world. Are you running on a server?");
                return;
            }
            EntityManager = World.EntityManager;
            try
            {
                DebugEventsSystem = World.GetExistingSystemManaged<DebugEventsSystem>();
            }
            catch (Exception ex)
            {
                _log?.LogError($"NazarickAura: Failed to obtain DebugEventsSystem: {ex.Message}");
            }
        }

        private static World? FindWorldByName(string name)
        {
            // Unity DOTS maintains a static list of all worlds; iterate to find the server world
            foreach (var world in Unity.Entities.World.s_AllWorlds)
            {
                if (world.Name == name)
                {
                    return world;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the logger associated with the server context. Used by helpers to log errors.
        /// </summary>
        public static BepInEx.Logging.ManualLogSource? GetLogger()
        {
            return _log;
        }
    }

    /// <summary>
    ///     NazarickAura plugin entry point. This class encapsulates all
    ///     functionality for reading configuration, registering chat commands and
    ///     applying or removing buffs. It is declared in a single file for
    ///     convenience as requested. The namespaces and types mirror those from
    ///     the modular implementation.
    /// </summary>
    [BepInPlugin(GUID, Name, Version)]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    public class NazarickAuraPlugin : BasePlugin
    {
        public const string GUID = "user.NazarickAura";
        public const string Name = "NazarickAura";
        public const string Version = "1.0.0";

        // We no longer use Harmony to patch methods. Instead, we rely on the
        // VampireCommandFramework to register commands at load time.

        /// <summary>
        /// Called by BepInEx when the plugin is loaded. We use this to load
        /// configuration and apply Harmony patches. This replaces the Mono
        /// specific Awake() method used in previous versions.
        /// </summary>
        public override void Load()
        {
            Log.LogInfo($"[{Name}] Carregando configuração de auras...");
            AuraManager.LoadAuras();
            // Inicialização tardia: tenta inicializar o contexto do servidor até o mundo estar disponível
            System.Threading.Tasks.Task.Run(async () =>
            {
                while (ServerContext.World == null)
                {
                    ServerContext.Initialize(Log);
                    if (ServerContext.World == null)
                        await System.Threading.Tasks.Task.Delay(1000); // Espera 1 segundo antes de tentar de novo
                }
                // Só registra comandos após o mundo estar disponível
                CommandRegistry.RegisterAll();
                Log.LogInfo($"[{Name}] Inicializado com sucesso.");
            });
        }

        /// <summary>
        /// Optional cleanup. When BepInEx unloads the plugin, unpatch Harmony
        /// methods to avoid leaving patched state behind. BepInEx calls this
        /// method if implemented.
        /// </summary>
        public override bool Unload()
        {
            // Unregister commands when the plugin is unloaded to clean up
            CommandRegistry.UnregisterAssembly();
            return true;
        }
    }

    /// <summary>
    ///     Represents a single aura definition loaded from JSON. Each aura has a
    ///     human‑friendly name, the prefab ID of the underlying buff, and an
    ///     index that the player can reference via the chat command.
    /// </summary>
    public class AuraConfigItem
    {
        public string Aura { get; set; } = string.Empty;
        public int Prefab { get; set; }
        public int Numero { get; set; }
    }

    /// <summary>
    ///     Manages loading and saving the aura configuration file. The config
    ///     consists of a list of AuraConfigItem entries stored in JSON format.
    ///     If the file does not exist at startup a default configuration with a
    ///     single aura entry is created and written to disk.
    /// </summary>
    public static class AuraManager
    {
        private static List<AuraConfigItem> _auras = new();
        public static IReadOnlyList<AuraConfigItem> Auras => _auras;
        private static string ConfigPath => Path.Combine(Paths.ConfigPath, "NazarickAura.auras.json");

        public static void LoadAuras()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    _auras = new List<AuraConfigItem>
                {
                    new AuraConfigItem
                    {
                        Aura = "Demoniaca",
                        Prefab = -93395631,
                        Numero = 1
                    }
                };
                    SaveAuras();
                }
                else
                {
                    var json = File.ReadAllText(ConfigPath);
                    _auras = JsonSerializer.Deserialize<List<AuraConfigItem>>(json) ?? new List<AuraConfigItem>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NazarickAura] Failed to load auras: {ex.Message}");
                _auras = new List<AuraConfigItem>();
            }
        }

        public static void SaveAuras()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(_auras, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NazarickAura] Failed to save auras: {ex.Message}");
            }
        }

        public static AuraConfigItem? FindAura(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                return null;
            }
            if (int.TryParse(arg, out var numero))
            {
                return _auras.FirstOrDefault(a => a.Numero == numero);
            }
            return _auras.FirstOrDefault(a => string.Equals(a.Aura, arg, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    ///     Helper routines for adding and removing buffs on characters. This class
    ///     is copied from the original KindredCommands implementation so that
    ///     the toggle logic remains identical to the vanilla buff and debuff
    ///     commands.
    /// </summary>
    internal static class Buffs
    {
        public static bool AddBuff(Entity User, Entity Character, PrefabGUID buffPrefab, int duration = 0, bool immortal = true)
        {
            var em = ServerContext.EntityManager;
            var des = ServerContext.DebugEventsSystem;
            var buffEvent = new ApplyBuffDebugEvent()
            {
                BuffPrefabGUID = buffPrefab
            };
            var fromCharacter = new FromCharacter()
            {
                User = User,
                Character = Character
            };
            if (!BuffUtility.TryGetBuff(em, Character, buffPrefab, out Entity buffEntity))
            {
                des.ApplyBuff(fromCharacter, buffEvent);
                if (BuffUtility.TryGetBuff(em, Character, buffPrefab, out buffEntity))
                {
                    // Remove componentes se existirem
                    if (em.HasComponent<CreateGameplayEventsOnSpawn>(buffEntity))
                        em.RemoveComponent<CreateGameplayEventsOnSpawn>(buffEntity);
                    if (em.HasComponent<GameplayEventListeners>(buffEntity))
                        em.RemoveComponent<GameplayEventListeners>(buffEntity);

                    if (immortal)
                    {
                        if (!em.HasComponent<Buff_Persists_Through_Death>(buffEntity))
                            em.AddComponent<Buff_Persists_Through_Death>(buffEntity);
                        if (em.HasComponent<RemoveBuffOnGameplayEvent>(buffEntity))
                            em.RemoveComponent<RemoveBuffOnGameplayEvent>(buffEntity);
                        if (em.HasComponent<RemoveBuffOnGameplayEventEntry>(buffEntity))
                            em.RemoveComponent<RemoveBuffOnGameplayEventEntry>(buffEntity);
                    }
                    if (duration > -1 && duration != 0)
                    {
                        if (!em.HasComponent<LifeTime>(buffEntity))
                            em.AddComponent<LifeTime>(buffEntity);
                        var lifetime = em.GetComponentData<LifeTime>(buffEntity);
                        lifetime.Duration = duration;
                        lifetime.EndAction = LifeTimeEndAction.Destroy;
                        em.SetComponentData(buffEntity, lifetime);
                    }
                    else if (duration == -1)
                    {
                        if (em.HasComponent<LifeTime>(buffEntity))
                        {
                            var lifetime = em.GetComponentData<LifeTime>(buffEntity);
                            lifetime.EndAction = LifeTimeEndAction.None;
                            em.SetComponentData(buffEntity, lifetime);
                        }
                        if (em.HasComponent<RemoveBuffOnGameplayEvent>(buffEntity))
                            em.RemoveComponent<RemoveBuffOnGameplayEvent>(buffEntity);
                        if (em.HasComponent<RemoveBuffOnGameplayEventEntry>(buffEntity))
                            em.RemoveComponent<RemoveBuffOnGameplayEventEntry>(buffEntity);
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public static void RemoveBuff(Entity Character, PrefabGUID buffPrefab)
        {
            var em = ServerContext.EntityManager;
            // Only attempt to remove the buff if the character exists
            if (!em.Exists(Character)) return;
            if (BuffUtility.TryGetBuff(em, Character, buffPrefab, out var buffEntity) && em.Exists(buffEntity))
            {
                DestroyUtility.Destroy(em, buffEntity, DestroyDebugReason.TryRemoveBuff);
            }
        }
    }

    /// <summary>
    ///     Implements the .aura chat command. Players with appropriate
    ///     permissions can toggle named or numbered aura buffs defined in the
    ///     configuration file. When invoked with the special argument "list" a
    ///     numbered list of all available auras is displayed.
    /// </summary>
    // Nest the command class inside the NazarickAura namespace to avoid mixing
    // file‑scoped and block namespaces. This class registers the chat command
    // that toggles auras and lists available auras.
    namespace Commands
    {
        internal static class AuraCommands
        {
            [Command("aura", "ar", "a", description: "Ativa ou desativa uma aura por número ou nome; use .aura list para listar todas as auras", adminOnly: true)]
            public static void AuraCommand(ChatCommandContext ctx, string arg)
            {
                if (string.IsNullOrWhiteSpace(arg))
                {
                    ctx.Reply("Uso: .aura <número|nome|list>");
                    return;
                }
                if (string.Equals(arg, "list", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "lista", StringComparison.OrdinalIgnoreCase))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Lista Completa:");
                    foreach (var aura in AuraManager.Auras)
                    {
                        sb.AppendLine($"{aura.Numero}. {aura.Aura}");
                    }
                    ctx.Reply(sb.Length > 0 ? sb.ToString() : "Nenhuma aura está configurada.");
                    return;
                }
                var auraItem = AuraManager.FindAura(arg);
                if (auraItem == null)
                {
                    ctx.Reply($"Aura '{arg}' não encontrada. Use .aura list para ver as opções.");
                    return;
                }
                var buffPrefab = new PrefabGUID(auraItem.Prefab);
                var userEntity = ctx.Event.SenderUserEntity;
                var charEntity = ctx.Event.SenderCharacterEntity;
                // Defensive checks to avoid crashing the server when entities are invalid
                var em = ServerContext.EntityManager;
                var world = ServerContext.World;
                if (world == null)
                {
                    ctx.Reply("[NazarickAura] Mundo do servidor não inicializado.");
                    return;
                }
                if (userEntity.Index < 0 || charEntity.Index < 0 || !em.Exists(charEntity) || !em.Exists(userEntity))
                {
                    ctx.Reply("[NazarickAura] Não foi possível resolver sua entidade de personagem/usuário. Tente relogar.");
                    return;
                }

                try
                {
                    // Check if the buff is already applied
                    if (BuffUtility.TryGetBuff(em, charEntity, buffPrefab, out var buffEntity))
                    {
                        Buffs.RemoveBuff(charEntity, buffPrefab);
                        ctx.Reply($"A aura {auraItem.Aura} foi desativada.");
                    }
                    else
                    {
                        if (Buffs.AddBuff(userEntity, charEntity, buffPrefab, duration: -1, immortal: true))
                        {
                            ctx.Reply($"A aura {auraItem.Aura} foi ativada.");
                        }
                        else
                        {
                            ctx.Reply("[NazarickAura] Falha ao aplicar a aura (verifique o log do servidor).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ServerContext.GetLogger()?.LogError($"[NazarickAura] Exceção no toggle de aura: {ex}");
                    ctx.Reply("[NazarickAura] Ocorreu um erro ao alternar a aura.");
                }
            }

            [Command("removeaura", "ra", description: "Remove todas as auras ativas do personagem.", adminOnly: true)]
            public static void RemoveAuraCommand(ChatCommandContext ctx)
            {
                var em = ServerContext.EntityManager;
                var world = ServerContext.World;
                var charEntity = ctx.Event.SenderCharacterEntity;
                if (world == null)
                {
                    ctx.Reply("[NazarickAura] Mundo do servidor não inicializado.");
                    return;
                }
                if (charEntity.Index < 0 || !em.Exists(charEntity))
                {
                    ctx.Reply("[NazarickAura] Não foi possível resolver sua entidade de personagem. Tente relogar.");
                    return;
                }
                int removed = 0;
                foreach (var aura in AuraManager.Auras)
                {
                    var buffPrefab = new PrefabGUID(aura.Prefab);
                    if (BuffUtility.TryGetBuff(em, charEntity, buffPrefab, out var buffEntity))
                    {
                        Buffs.RemoveBuff(charEntity, buffPrefab);
                        removed++;
                    }
                }
                if (removed > 0)
                    ctx.Reply($"Todas as auras ({removed}) foram removidas.");
                else
                    ctx.Reply("Nenhuma aura ativa foi encontrada para remover.");
            }
        }
    }
}