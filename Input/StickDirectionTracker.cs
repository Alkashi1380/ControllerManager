namespace ControllerManager
{
    // Transforme un stick en 4 « boutons » directionnels (haut, bas, gauche, droite ; diagonales = 2 directions).
    // Hystérésis : une direction s'enclenche à 0.5 et ne se relâche que sous 0.35, pour éviter le clignotement au seuil.
    public sealed class StickDirectionTracker
    {
        public const int Up = 0, Down = 1, Left = 2, Right = 3;
        public static readonly string[] Names = { "up", "down", "left", "right" };

        public const float PressThreshold = 0.5f;
        public const float ReleaseThreshold = 0.35f;

        private readonly bool[] _pressed = new bool[4];

        public bool IsPressed(int direction) => _pressed[direction];

        // y négatif = haut (convention des deux boucles d'entrées)
        public List<(int Direction, bool Pressed)> Update(float x, float y, double deadzone)
        {
            if (Math.Sqrt(x * x + y * y) < deadzone)
            {
                x = 0;
                y = 0;
            }

            float[] amount = { -y, y, -x, x };
            var changes = new List<(int, bool)>();
            for (int d = 0; d < 4; d++)
            {
                bool next = _pressed[d] ? amount[d] > ReleaseThreshold : amount[d] > PressThreshold;
                if (next != _pressed[d])
                {
                    _pressed[d] = next;
                    changes.Add((d, next));
                }
            }
            return changes;
        }

        // Directions qui étaient enfoncées (à relâcher par l'appelant)
        public List<int> ReleaseAll()
        {
            var released = new List<int>();
            for (int d = 0; d < 4; d++)
            {
                if (_pressed[d]) released.Add(d);
                _pressed[d] = false;
            }
            return released;
        }
    }
}
