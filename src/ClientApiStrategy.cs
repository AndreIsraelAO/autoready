using System;
using System.Diagnostics;

namespace AutoReady
{
    // Juegos con API local del cliente (League of Legends): se le pregunta la fase al cliente y, con una partida
    // encontrada, se acepta por su API. No mira la pantalla, no mueve el mouse ni trae la ventana al frente, y en
    // seleccion de campeones o en partida no manda nada.
    class ClientApiStrategy : GameStrategy
    {
        readonly Lcu lcu;
        string phase;            // ultima fase leida (null = desconocida)
        bool phaseKnown;         // ya se leyo una vez desde que se empezo a vigilar
        volatile bool inMatch;

        public ClientApiStrategy(GameProfile game, IGameHost host) : this(game, host, new Lcu()) { }

        // Con una API propia (las pruebas usan un cliente falso).
        public ClientApiStrategy(GameProfile game, IGameHost host, Lcu lcu) : base(game, host) { this.lcu = lcu; }

        // El cliente no necesita ventana visible: alcanza con que el proceso este abierto.
        public override bool IsRunning()
        {
            Process[] ps = Process.GetProcessesByName(Game.Process);
            foreach (Process p in ps) p.Dispose();
            return ps.Length > 0;
        }

        public override void Reset()
        {
            base.Reset();
            phase = null;
            phaseKnown = false;
            inMatch = false;
        }

        public override void Deactivated() { inMatch = false; }

        public override bool InMatch { get { return inMatch; } }

        public override int Tick()
        {
            string now = lcu.Phase();
            if (now != phase || !phaseKnown)
            {
                Log.Write(Game.Name + ": fase " + (phase ?? "?") + " -> " + (now ?? "?"));
                // Arranco la seleccion de campeones (o la partida): mismo aviso que con GSI. La primera lectura no cuenta
                // como cambio (si ya estaba en partida al empezar a vigilarlo, no es una partida nueva).
                if (phaseKnown && Lcu.IsInMatch(now) && !Lcu.IsInMatch(phase)) Host.MatchStateChanged(Game, null, now);
                else if (phaseKnown && !Lcu.IsInMatch(now) && Lcu.IsInMatch(phase)) Host.MatchStateChanged(Game, phase, null);
                phase = now;
                phaseKnown = true;
            }
            inMatch = Lcu.IsInMatch(now);
            if (inMatch) { Host.SetState(AppState.InMatch); return 1000; }
            Host.SetState(AppState.Searching);
            if (now != "ReadyCheck" || InCooldown) return 1000;
            if (!lcu.ReadyCheckPending()) return 500;   // ya la acepto (o la rechazo) el jugador
            if (Host.Paused) return 500;                 // se pudo pausar mientras se consultaba
            if (!lcu.Accept()) { Log.Write(Game.Name + ": no pude aceptar por la API del cliente"); return 500; }
            StartCooldown();
            Host.MatchAccepted(Game, "por la API del cliente");
            return 1000;
        }
    }
}
