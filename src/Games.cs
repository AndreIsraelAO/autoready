using System;
using System.Collections.Generic;

namespace AutoReady
{
    // Todo lo que cambia de un juego a otro: proceso, donde va la config de GSI, como leer el estado
    // y como es el boton de aceptar.
    class GameProfile
    {
        public string Id;              // clave en el ini: "dota2.HueMin"
        public string Name;
        public string Process;         // nombre del proceso sin .exe
        public int AppId;              // viene en "provider.appid" de cada POST de GSI
        public string CfgDir;          // carpeta de la config de GSI, relativa a la biblioteca de Steam
        public string StateKey;        // clave del JSON de GSI que indica la fase de la partida
        public string LaunchOptionHint;   // null si GSI funciona sin opciones de lanzamiento
        // Aceptar apretando Enter en vez de hacer click: no depende de donde esta el boton (resolucion, escala,
        // monitor). Si despues del Enter el popup sigue en pantalla, se hace click igual.
        public bool AcceptWithEnter;
        // Opcion de lanzamiento que el juego necesita para mandar GSI (null si no hace falta).
        public string GsiLaunchOption;
        // Leer el estado de Steam (Rich Presence) para saber si busca partida o esta jugando, sin configurar nada.
        public bool UsesPresence;
        public string MatchStartedTitle, MatchStartedText;
        public Func<Detector.Settings> DefaultDetector;

        public static readonly GameProfile Dota2 = new GameProfile
        {
            Id = "dota2",
            Name = "Dota 2",
            Process = "dota2",
            AppId = 570,
            CfgDir = @"steamapps\common\dota 2 beta\game\dota\cfg\gamestate_integration",
            StateKey = "game_state",
            LaunchOptionHint = "En Steam: Dota 2 > Propiedades > Opciones de lanzamiento, agrega -gamestateintegration y reinicia Dota.",
            AcceptWithEnter = true,
            GsiLaunchOption = "-gamestateintegration",
            UsesPresence = true,
            MatchStartedTitle = "¡Empezo el pick!",
            MatchStartedText = "Ya estan eligiendo heroes. ¡Volve!",
            // Boton ACCEPT turquesa: H ~154, S ~0.30-0.35, V ~0.35.
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
            LaunchOptionHint = null,
            MatchStartedTitle = "¡La partida esta cargando!",
            MatchStartedText = "Tu partida de CS2 arranca. ¡Volve!",
            // Boton ACCEPT verde mas saturado y brillante que el de Dota.
            DefaultDetector = () => new Detector.Settings
            {
                HueMin = 85, HueMax = 150, SatMin = 0.35, ValMin = 0.40,
                MinWidthFrac = 0.06, MaxWidthFrac = 0.40,
                MinHeightFrac = 0.025, MaxHeightFrac = 0.15,
                MinAspect = 1.8, MaxAspect = 9.0,
            },
        };

        public static readonly List<GameProfile> All = new List<GameProfile> { Dota2, Cs2 };

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
