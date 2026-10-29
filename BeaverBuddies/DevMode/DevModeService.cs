using System.Collections.Generic;
using System.Linq;
using Timberborn.BeaversUI;
using Timberborn.BotsUI;
using Timberborn.Debugging;
using Timberborn.DebuggingUI;
using Timberborn.MortalSystem;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using Timberborn.WaterBrushesUI;
using Timberborn.WeatherSystem;

namespace BeaverBuddies.DevMode
{
    /// <summary>
    /// Holds references to the game's developer-mode services, so that
    /// dev-mode actions recorded by one player can be replayed on every
    /// player (see DevModeEvents and DevModeUIEvents).
    /// </summary>
    public class DevModeService : RegisteredSingleton, ILoadableSingleton
    {
        private readonly EventBus _eventBus;

        public DevModeManager DevModeManager { get; }
        public DevPanel DevPanel { get; }
        public TimeFastForwarder TimeFastForwarder { get; }
        public WeatherFastForwarder WeatherFastForwarder { get; }
        public BeaverGeneratorTool BeaverGeneratorTool { get; }
        public BotGeneratorTool BotGeneratorTool { get; }
        public WaterHeightBrushTool WaterHeightBrushTool { get; }
        // Only bound by the game as an IDevModule, so it's looked up among them
        public CharacterKiller CharacterKiller { get; }

        public DevModeService(
            EventBus eventBus,
            DevModeManager devModeManager,
            DevPanel devPanel,
            TimeFastForwarder timeFastForwarder,
            WeatherFastForwarder weatherFastForwarder,
            BeaverGeneratorTool beaverGeneratorTool,
            BotGeneratorTool botGeneratorTool,
            WaterHeightBrushTool waterHeightBrushTool,
            IEnumerable<IDevModule> devModules
        )
        {
            _eventBus = eventBus;
            DevModeManager = devModeManager;
            DevPanel = devPanel;
            TimeFastForwarder = timeFastForwarder;
            WeatherFastForwarder = weatherFastForwarder;
            BeaverGeneratorTool = beaverGeneratorTool;
            BotGeneratorTool = botGeneratorTool;
            WaterHeightBrushTool = waterHeightBrushTool;
            CharacterKiller = devModules.OfType<CharacterKiller>().FirstOrDefault();
        }

        public void Load()
        {
            _eventBus.Register(this);
            Plugin.Log($"Dev mode support loaded; character killer found: {CharacterKiller != null}");
        }

        [OnEvent]
        public void OnDevModeToggled(DevModeToggledEvent devModeToggledEvent)
        {
            // Dev mode itself is a local setting (each player toggles their own
            // panel), but the actions taken from it are shared with everyone.
            Plugin.Log($"Dev mode {(devModeToggledEvent.Enabled ? "enabled" : "disabled")} " +
                $"by the local player; game-changing dev actions will be shared with all players");
        }
    }
}
