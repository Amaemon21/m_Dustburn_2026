using System.Collections.Generic;

public sealed partial class StandingWaterRegions
{
    private void DropFragments(Window window, bool[] flooded)
    {
        var label = new int[window.Count];
        var sizes = new List<int> { 0 };
        var queue = new Queue<int>();

        for (int start = 0; start < window.Count; start++)
        {
            if (!flooded[start] || label[start] != 0)
                continue;

            int id = sizes.Count;
            int size = 0;

            label[start] = id;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int column = current % window.Width;
                int row = current / window.Width;

                size++;

                for (int direction = 0; direction < 4; direction++)
                {
                    int c = column + StepX[direction];
                    int r = row + StepZ[direction];

                    if (c < 0 || r < 0 || c >= window.Width || r >= window.Height)
                        continue;

                    int next = r * window.Width + c;

                    if (!flooded[next] || label[next] != 0)
                        continue;

                    label[next] = id;
                    queue.Enqueue(next);
                }
            }

            sizes.Add(size);
        }

        int main = 0;

        for (int id = 1; id < sizes.Count; id++)
        {
            if (sizes[id] > sizes[main])
                main = id;
        }

        var keep = new bool[sizes.Count];

        for (int id = 1; id < sizes.Count; id++)
        {
            keep[id] = id == main || sizes[id] * _step * _step >= SATELLITE_AREA;

            if (!keep[id])
                Fragments++;
        }

        for (int n = 0; n < window.Count; n++)
        {
            if (flooded[n] && !keep[label[n]])
                flooded[n] = false;
        }
    }

    private void Claim(Window window, bool[] flooded, int body)
    {
        for (int n = 0; n < window.Count; n++)
        {
            if (flooded[n])
                _owner[Global(window, n)] = (short)body;
        }
    }

    private int Neighbours(Window window, int local, int body)
    {
        int column = local % window.Width;
        int row = local / window.Width;
        int count = 0;

        for (int direction = 0; direction < 4; direction++)
        {
            int c = column + StepX[direction];
            int r = row + StepZ[direction];

            if (c < 0 || r < 0 || c >= window.Width || r >= window.Height)
                continue;

            if (_owner[Global(window, r * window.Width + c)] == body)
                count++;
        }

        return count;
    }

    private void SinkIslets(Window window, int body, float surface, float shelf)
    {
        var seen = new bool[window.Count];
        var queue = new Queue<int>();
        var members = new List<int>();

        for (int start = 0; start < window.Count; start++)
        {
            if (seen[start] || _owner[Global(window, start)] == body || !Neighbouring(window, start, body))
                continue;

            members.Clear();
            queue.Clear();
            seen[start] = true;
            queue.Enqueue(start);

            bool closed = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                members.Add(current);

                if (_owner[Global(window, current)] != WaterMap.OWNER_SEA || _barred[Global(window, current)] || window.Ground[current] >= surface + shelf || members.Count > IsletNodes)
                    closed = false;

                int column = current % window.Width;
                int row = current / window.Width;

                for (int direction = 0; direction < 4; direction++)
                {
                    int c = column + StepX[direction];
                    int r = row + StepZ[direction];

                    if (c < 0 || r < 0 || c >= window.Width || r >= window.Height)
                    {
                        closed = false;
                        continue;
                    }

                    int next = r * window.Width + c;

                    if (seen[next] || _owner[Global(window, next)] == body)
                        continue;

                    seen[next] = true;

                    if (members.Count + queue.Count <= IsletNodes)
                        queue.Enqueue(next);
                    else
                        closed = false;
                }
            }

            if (!closed)
                continue;

            Islets++;

            foreach (int member in members)
            {
                int node = Global(window, member);
                _owner[node] = (short)body;
                _sunk.Add(node);
            }
        }
    }

    private bool Neighbouring(Window window, int local, int body)
    {
        return Neighbours(window, local, body) > 0;
    }
}
