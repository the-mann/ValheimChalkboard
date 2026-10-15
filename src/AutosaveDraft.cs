using System;

namespace ValheimWhiteboard
{
    // Shared by the actual editor and managed tests; no engine/network doubles needed.
    public sealed class AutosaveDraft
    {
        public byte[] Pixels, Baseline;
        public int Revision, Failures;
        public bool Pending, Dirty, Closing;
        public float SubmittedAt, ChangedAt;
        public AutosaveDraft(byte[] pixels, int revision)
        { Pixels = (byte[])pixels.Clone(); Baseline = (byte[])pixels.Clone(); Revision = revision; }
        public void Changed(float now) { Dirty = true; Failures = 0; ChangedAt = now; }
        public bool ShouldSubmit(float now, bool drawing) => !Pending && Dirty && !drawing && Failures < 3 && now - ChangedAt >= .4f;
        public bool BeginSubmit(byte[] latest, int revision, float now)
        {
            if (Pending || !Dirty) return false;
            if (revision != Revision)
            { Pixels = BoardData.MergeDraft(latest, Baseline, Pixels); Baseline = (byte[])latest.Clone(); Revision = revision; }
            Pending = true; SubmittedAt = now; return true;
        }
        public bool RequestClose()
        { if (!Dirty && !Pending) return true; Closing = true; Failures = 0; return false; }
        public bool Acknowledge(bool accepted, int revision, float now)
        {
            if (!Pending) return false;
            Pending = false;
            if (accepted)
            { Revision = revision; Baseline = (byte[])Pixels.Clone(); Dirty = false; Failures = 0; return Closing; }
            Failures++; Closing = false; ChangedAt = now; return false;
        }
        public bool CheckTimeout(float now)
        {
            if (!Pending || now - SubmittedAt <= 10f) return false;
            Pending = false; Closing = false; Failures = 3; return true;
        }
    }
}
