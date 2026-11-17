using System;

namespace RazerPadFan {
internal static partial class Program {
  enum FanMode { Automatic, Quiet, Manual }

  static FanMode ParseFanMode(string name) {
    if (string.Equals(name, "quiet", StringComparison.OrdinalIgnoreCase)) return FanMode.Quiet;
    if (string.Equals(name, "manual", StringComparison.OrdinalIgnoreCase)) return FanMode.Manual;
    return FanMode.Automatic;
  }

  // Fan units are 50 RPM. All times passed to the policy come from a monotonic clock.
  sealed class FanController {
    const int AutomaticCap = 2300 / FACTOR, QuietCap = 1900 / FACTOR;
    const int ReviewMs = 120000, WarmHoldMs = 10000, CoolHoldMs = 60000;
    const int BoostStepMs = 5000, SevereStepMs = 2000, RecoveryStepMs = 10000;
    const double Warm = 78, Immediate = 82, Severe = 90, Recovered = 69;

    FanMode? previousMode;
    int normalUnit, command = -1;
    long nextNormalReview, lastSample = -1, warmSince = -1, coolSince = -1, lastEmergencyReview;
    double filtered = -1, trendBaseline = -1, emergencyReviewTemp;
    bool hasNormalEstimate;

    public bool Emergency { get; private set; }
    public bool HasTemperature { get; private set; }

    public static bool ValidTemperature(double value) { return value > 0 && value <= 125; }

    public int Update(long now, double cpu, double gpu, FanMode mode, int manualUnit) {
      double hot = Math.Max(ValidTemperature(cpu) ? cpu : -1, ValidTemperature(gpu) ? gpu : -1);
      HasTemperature = hot > 0;
      bool modeChanged = mode != previousMode;
      previousMode = mode;

      if (HasTemperature) ObserveTemperature(now, hot);
      else {
        lastSample = warmSince = coolSince = -1;
        emergencyReviewTemp = double.NaN;
      }

      if (mode == FanMode.Manual) normalUnit = Limit(manualUnit, MIN_UNIT, MAX_UNIT);
      else if (modeChanged || HasTemperature && (!hasNormalEstimate || now >= nextNormalReview)) {
        int ceiling = mode == FanMode.Quiet ? QuietCap : AutomaticCap;
        if (modeChanged && !HasTemperature) {
          normalUnit = Math.Min(ceiling, command >= 0 ? command : 1500 / FACTOR);
          hasNormalEstimate = false;
        } else {
          int estimate = EstimateNormalUnit(mode, ceiling);
          if (modeChanged || !hasNormalEstimate || Math.Abs(estimate - normalUnit) >= 3)
            normalUnit = estimate;
          hasNormalEstimate = true;
        }
        nextNormalReview = now + ReviewMs;
      }

      // A temperature override may hold the fan above the normal target, never below it.
      command = Math.Max(command, normalUnit);
      if (!HasTemperature) {
        if (!Emergency && (modeChanged || mode == FanMode.Manual)) command = normalUnit;
        return command;
      }

      if (hot >= Warm) {
        if (warmSince < 0) warmSince = now;
      } else warmSince = -1;

      if (!Emergency && (hot >= Immediate || warmSince >= 0 && now - warmSince >= WarmHoldMs)) {
        Emergency = true;
        lastEmergencyReview = now - BoostStepMs;
        emergencyReviewTemp = hot;
      }
      if (!Emergency) return command = normalUnit;

      if (hot <= Recovered && filtered <= Recovered + 3) {
        if (coolSince < 0) coolSince = now;
        if (now - coolSince >= CoolHoldMs && now - lastEmergencyReview >= RecoveryStepMs) {
          lastEmergencyReview = now;
          command = Math.Max(normalUnit, command - 3);
          Emergency = command > normalUnit;
        }
      } else {
        coolSince = -1;
        EscalateIfNeeded(now, hot);
      }
      return command;
    }

    void ObserveTemperature(long now, double hot) {
      long elapsed = now - lastSample;
      if (lastSample < 0 || elapsed > 10000) {
        filtered = trendBaseline = hot;
        warmSince = coolSince = -1;
        emergencyReviewTemp = hot;
      } else {
        filtered += (hot - filtered) * (1 - Math.Exp(-elapsed / 18000.0));
        trendBaseline += (hot - trendBaseline) * (1 - Math.Exp(-elapsed / 45000.0));
      }
      lastSample = now;
    }

    int EstimateNormalUnit(FanMode mode, int ceiling) {
      double forecast = filtered + Math.Max(0, Math.Min(5, filtered - trendBaseline));
      double rpm = mode == FanMode.Quiet ? 900 + (forecast - 45) * 35 : 1000 + (forecast - 45) * 50;
      int units = (int)Math.Round(rpm / 100) * (100 / FACTOR);
      return Limit(units, MIN_UNIT, ceiling);
    }

    void EscalateIfNeeded(long now, double hot) {
      int cadence = hot >= Severe ? SevereStepMs : BoostStepMs;
      if (now - lastEmergencyReview < cadence) return;
      bool cooling = hot < emergencyReviewTemp - 0.5;
      emergencyReviewTemp = hot;
      lastEmergencyReview = now;
      if (hot < Warm || cooling) return;

      int ceiling = hot >= Severe ? MAX_UNIT : hot >= Immediate ? 2800 / FACTOR : 2500 / FACTOR;
      int step = hot >= Severe ? 5 : 3;
      if (command < ceiling) command = Math.Min(ceiling, command + step);
    }
  }
}
}
