using System;

namespace AutoReady
{
    // Lo que una estrategia de juego necesita de la app. TrayApp lo implementa: estado, pausa, avisos y la configuracion.
    // Las estrategias no tocan la interfaz directamente: todo pasa por aca.
    interface IGameHost
    {
        Config Cfg { get; }
        Gsi Gsi { get; }
        bool Paused { get; }
        void SetPaused(bool paused);
        void SetState(AppState state);
        void Notify(string title, string text, string button = null, Action onButton = null, Action onClick = null);
        // Se acepto una partida (anota la hora, suena, avisa en la PC y el celular). onClick: que hace tocar el aviso.
        void MatchAccepted(GameProfile game, string how, Action onClick = null);
        // Empezo (newState != null y oldState == null) o termino (newState == null) una partida.
        void MatchStateChanged(GameProfile game, string oldState, string newState);
        // Ultima vez que se acepto una partida en esta sesion de juego (MinValue = ninguna o ya empezo la partida).
        DateTime LastAccept { get; }
        void ForgetLastAccept();
    }

    // Como se vigila y se acepta un juego. Cada juego elige la suya en su GameProfile (Games.cs):
    //   ScreenStrategy    - mira la pantalla y hace click o Enter (Dota 2, CS2), con GSI y estado de Steam para no
    //                       tocar nada en partida.
    //   ClientApiStrategy - le pregunta al cliente por su API local y acepta por ahi (League of Legends).
    // TrayApp solo orquesta: elige que juego esta abierto, maneja la pausa y llama a Tick.
    //
    // Regla de oro para cualquier estrategia nueva: nunca actuar si el juego esta en partida o si Host.Paused, y
    // volver a revisarlo justo antes de cada accion.
    //
    // Hilos: IsRunning, Reset, Deactivated y Tick corren en el hilo del escaneo. Ojo: ResetDetection (pausa desde el
    // panel o el atajo), InMatch (actualizador, panel), OnWindowFlashed (interfaz) y OnPresenceChanged (estado de
    // Steam) pueden llegar desde otros hilos: lo que tocan tiene que ser seguro (volatile o una bandera que lea Tick).
    abstract class GameStrategy
    {
        public readonly GameProfile Game;
        protected readonly IGameHost Host;
        protected DateTime CooldownUntil = DateTime.MinValue;

        protected GameStrategy(GameProfile game, IGameHost host)
        {
            Game = game;
            Host = host;
        }

        protected Config Cfg { get { return Host.Cfg; } }

        // El juego esta abierto (se llama en cada vuelta del loop, tiene que ser barato).
        public abstract bool IsRunning();

        // Se abrio el juego: empieza de cero.
        public virtual void Reset() { ResetDetection(); }

        // Pausa o reanudacion: se descarta lo que se venia detectando (no se acepta con datos de antes de la pausa).
        public virtual void ResetDetection() { }

        // Se dejo de vigilar este juego (se cerro o paso a otro).
        public virtual void Deactivated() { }

        // Una vuelta del loop con el juego abierto y sin pausa. Devuelve cuantos ms esperar hasta la proxima.
        public abstract int Tick();

        // En partida (o armandola): la app no se actualiza ni hace nada que moleste.
        public virtual bool InMatch { get { return false; } }

        // Falta un paso de configuracion (lo muestra el panel con el boton Configurar). Se consulta aunque el juego
        // este cerrado.
        public virtual bool NeedsSetup() { return false; }

        // Avisos del sistema que solo le importan a algunas estrategias.
        public virtual void OnWindowFlashed(IntPtr hwnd) { }
        public virtual void OnPresenceChanged(PresenceState old, PresenceState now, string text) { }

        // Empieza la espera despues de aceptar (para no volver a actuar sobre el mismo popup).
        protected void StartCooldown()
        {
            CooldownUntil = DateTime.Now.AddSeconds(Cfg.CooldownAfterAcceptSec);
        }

        protected bool InCooldown { get { return DateTime.Now < CooldownUntil; } }
    }
}
