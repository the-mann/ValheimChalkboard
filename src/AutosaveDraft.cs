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
        private byte[] submitted;
        private int generation, submittedGeneration;
        public AutosaveDraft(byte[] pixels, int revision)
        { Pixels = (byte[])pixels.Clone(); Baseline = (byte[])pixels.Clone(); Revision = revision; }
        public void Changed(float now) { Dirty = true; generation++; Failures = 0; ChangedAt = now; }
        public bool ShouldSubmit(float now, bool drawing) => !Pending && Dirty && Failures < 3 && (Closing || now - SubmittedAt >= .1f);
        public bool BeginSubmit(byte[] latest, int revision, float now)
        {
            if (Pending || !Dirty) return false;
            // Stream complete snapshots. Server receive order decides the winning image.
            Revision = revision; submitted = (byte[])Pixels.Clone(); submittedGeneration = generation;
            Pending = true; SubmittedAt = now; return true;
        }
        public bool Observe(byte[] latest, int revision)
        {
            if (Dirty || Pending || revision <= Revision) return false;
            Pixels = (byte[])latest.Clone(); Baseline = (byte[])latest.Clone(); Revision = revision; return true;
        }
        public bool RequestClose()
        { if (!Dirty && !Pending) return true; Closing = true; Failures = 0; return false; }
        public bool Acknowledge(bool accepted, int revision, float now)
        {
            if (!Pending) return false;
            Pending = false;
            if (accepted)
            { Revision = revision; Baseline = submitted; Dirty = generation != submittedGeneration; Failures = 0; return Closing && !Dirty; }
            Failures++; Closing = false; ChangedAt = now; return false;
        }
        public bool CheckTimeout(float now)
        {
            if (!Pending || now - SubmittedAt <= 10f) return false;
            Pending = false; Closing = false; Failures = 3; return true;
        }
    }
}

