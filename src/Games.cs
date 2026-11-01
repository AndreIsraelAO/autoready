using System;
using System.Collections.Generic;

namespace AutoReady
{
    // Todo lo que cambia de un juego a otro: proceso, GSI, estado de Steam, como es el boton de aceptar y con que
    // estrategia se acepta (NewStrategy). Lo que ve el usuario (avisos, panel) es generico y usa solo Name; sumar un
    // juego es agregar un perfil aca (y, si se acepta de otra forma, una estrategia nueva en GameStrategy.cs).
    class GameProfile
    {
        public string Id;              // clave en el ini: "dota2.HueMin"
        public string Name;
        public string Process;         // nombre del proceso sin .exe
        public int AppId;              // viene en "provider.appid" de cada POST de GSI
        public string CfgDir;          // carpeta de la config de GSI, relativa a la biblioteca de Steam
        public string StateKey;        // clave del JSON de GSI que indica la fase de la partida
        // Opcion de lanzamiento que el juego necesita para mandar GSI (null si no hace falta).
        public string GsiLaunchOption;
        // Aceptar apretando Enter en vez de hacer click: no depende de donde esta el boton (resolucion, escala,
        // monitor). Si despues del Enter el popup sigue en pantalla, se hace click igual.
        public bool AcceptWithEnter;
        // Estado de Steam (Rich Presence, en ingles): textos que indican que esta buscando partida o en el menu.
        // Cualquier otro estado del juego cuenta como "en partida". Vacio = el juego no usa el estado de Steam.
        public string[] PresenceFinding = new string[0], PresenceMenu = new string[0];
        public Func<Detector.Settings> DefaultDetector;
        // Como se vigila y se acepta este juego (GameStrategy.cs). Por defecto, mirando la pantalla.
        public Func<GameProfile, IGameHost, GameStrategy> NewStrategy = (g, h) => new ScreenStrategy(g, h);

        public bool UsesPresence { get { return PresenceFinding.Length > 0; } }

        public static readonly GameProfile Dota2 = new GameProfile
        {
            Id = "dota2",
            Name = "Dota 2",
            Process = "dota2",
            AppId = 570,
            CfgDir = @"steamapps\common\dota 2 beta\game\dota\cfg\gamestate_integration",
            StateKey = "game_state",
            GsiLaunchOption = "-gamestateintegration",
            AcceptWithEnter = true,
            // Textos oficiales: game/dota/pak01_dir/resource/localization/richpresence_english.txt
            PresenceFinding = new[] { "Finding A Match" },
            PresenceMenu = new[] { "Main Menu" },
            // Boton ACCEPT turquesa: H ~154, S ~0.36, V ~0.35 (medido en el boton real).
            DefaultDetector = () => new Detector.Settings(),
        };

        public static readonly GameProfile Cs2 = new GameProfile
        {
            Id = "cs2",
            Name = "CS2",
            Process = "cs2",
            AppId = 730,
            CfgDir = @"steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg",
            StateKey = "phase",
            // Boton ACCEPT verde brillante: H ~128, S ~0.69, V ~0.73 (medido en el boton real).
            DefaultDetector = () => new Detector.Settings
            {
                HueMin = 85, HueMax = 150, SatMin = 0.35, ValMin = 0.40,
                MinWidthFrac = 0.06, MaxWidthFrac = 0.40,
                MinHeightFrac = 0.025, MaxHeightFrac = 0.15,
                MinAspect = 1.8, MaxAspect = 9.0,
            },
        };

        // League of Legends: se acepta por la API local del cliente (Lcu.cs). Sin GSI, sin Steam y sin mirar la pantalla.
        public static readonly GameProfile League = new GameProfile
        {
            Id = "lol",
            Name = "League of Legends",
            Process = "LeagueClientUx",
            NewStrategy = (g, h) => new ClientApiStrategy(g, h),
            DefaultDetector = () => new Detector.Settings(),
        };

        // Deadlock: por pantalla y sin GSI (Valve no lo ofrece), asi que siempre corre en modo seguro. El color del boton
        // no esta medido: se detecta por recorte (templates\deadlock@<alto>.png) hasta tener una captura real del popup.
        public static readonly GameProfile Deadlock = new GameProfile
        {
            Id = "deadlock",
            Name = "Deadlock",
            Process = "project8",
            AppId = 1422450,
            DefaultDetector = () => new Detector.Settings(),
        };

        public static readonly List<GameProfile> All = new List<GameProfile> { Dota2, Cs2, League, Deadlock };

        public static GameProfile ById(string id)
        {
            return All.Find(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static GameProfile ByAppId(int appId)
        {
            return All.Find(g => g.AppId == appId);
        }
    }
}
