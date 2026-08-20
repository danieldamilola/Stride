import { useState } from "react";
import strideLogo from "@/imports/stride-browser-128x128.png";

// ─── Types ────────────────────────────────────────────────────────────────────
type StepId = "welcome" | "addressbar" | "privacy" | "extensions" | "search" | "appearance" | "done";
const STEP_IDS: StepId[] = ["welcome", "addressbar", "privacy", "extensions", "search", "appearance", "done"];

const ACCENTS = [
  "#7fb89a", // sage green (default)
  "#c9a86c", // warm gold
  "#7c9cbf", // steel blue
  "#9b8ec4", // muted violet
  "#e8e2d7", // cream
  "#c47b6a", // terracotta
];

const ENGINES = [
  { id: "ddg",   name: "DuckDuckGo",  tag: "no tracking",    note: "No profile, no history sold." },
  { id: "brave", name: "Brave Search", tag: "independent",   note: "Own index. No Google, no Bing." },
  { id: "start", name: "Startpage",   tag: "private Google", note: "Google results, zero tracking." },
  { id: "google",name: "Google",      tag: "tracks you",     note: "Most comprehensive. Extensive profiling." },
  { id: "bing",  name: "Bing",        tag: "tracks you",     note: "Microsoft telemetry on every query." },
];

// ─── Shared primitives ────────────────────────────────────────────────────────
function Row({ label, sub, right }: { label: string; sub?: string; right: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-6 py-4 border-b border-white/5 last:border-0">
      <div>
        <div className="text-sm text-[#e8e2d7]">{label}</div>
        {sub && <div className="text-xs text-[#6b6560] mt-0.5 leading-relaxed">{sub}</div>}
      </div>
      {right}
    </div>
  );
}

function Toggle({ on, onChange, accent }: { on: boolean; onChange: (v: boolean) => void; accent: string }) {
  return (
    <button
      onClick={() => onChange(!on)}
      className="relative w-10 h-[22px] rounded-full shrink-0 transition-colors duration-200 focus:outline-none"
      style={{ background: on ? accent : "#2a2824" }}
    >
      <span
        className="absolute top-[3px] left-[3px] w-4 h-4 bg-[#0e0d0b] rounded-full transition-transform duration-200"
        style={{ transform: on ? "translateX(18px)" : "translateX(0)" }}
      />
    </button>
  );
}

function Checkbox({ on, onChange, accent }: { on: boolean; onChange: (v: boolean) => void; accent: string }) {
  return (
    <button
      onClick={() => onChange(!on)}
      className="w-[18px] h-[18px] rounded-[4px] shrink-0 flex items-center justify-center transition-colors focus:outline-none"
      style={{ background: on ? accent : "transparent", border: `1.5px solid ${on ? accent : "#3a3630"}` }}
    >
      {on && (
        <svg width="10" height="10" viewBox="0 0 10 10" fill="none">
          <path d="M2 5l2.5 2.5 3.5-4" stroke="#0e0d0b" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
        </svg>
      )}
    </button>
  );
}

// ─── Progress ─────────────────────────────────────────────────────────────────
function Progress({ idx, accent }: { idx: number; accent: string }) {
  const total = STEP_IDS.length - 1;
  return (
    <div className="flex gap-1">
      {Array.from({ length: total }).map((_, i) => (
        <div
          key={i}
          className="h-[2px] rounded-full transition-all duration-500"
          style={{
            width: i === idx ? "2.5rem" : "0.5rem",
            background: i <= idx ? accent : "#2a2824",
          }}
        />
      ))}
    </div>
  );
}

// ─── Root ─────────────────────────────────────────────────────────────────────
export default function App() {
  const [idx, setIdx] = useState(0);
  const [accent, setAccent] = useState("#7fb89a");
  const [theme, setTheme] = useState<"system" | "light" | "dark">("dark");
  const [floatingBar, setFloatingBar] = useState(true);
  const [adBlock, setAdBlock] = useState(true);
  const [smartScreen, setSmartScreen] = useState(true);
  const [httpsForce, setHttpsForce] = useState(true);
  const [clearOnExit, setClearOnExit] = useState(false);
  const [hibernate, setHibernate] = useState(true);
  const [engine, setEngine] = useState("ddg");
  const [importFrom, setImportFrom] = useState<"chrome" | "edge" | "none">("chrome");
  const [importHistory, setImportHistory] = useState(true);
  const [importBookmarks, setImportBookmarks] = useState(true);
  const [setDefault, setSetDefault] = useState(true);

  const step = STEP_IDS[idx];
  const total = STEP_IDS.length - 1;
  const isDone = step === "done";

  const go = (d: 1 | -1) => setIdx(i => Math.max(0, Math.min(STEP_IDS.length - 1, i + d)));

  const shared = { accent };

  return (
    <div className="min-h-screen flex flex-col bg-[#0e0d0b] text-[#e8e2d7]">
      {/* Top bar */}
      <div className="flex items-center justify-between px-8 py-5 border-b border-white/5">
        <div className="flex items-center gap-2.5">
          <img src={strideLogo} alt="Stride" className="w-7 h-7 rounded-lg" />
          <span className="text-sm font-medium tracking-tight text-[#e8e2d7]/80">Stride</span>
        </div>
        {!isDone && <Progress idx={idx} accent={accent} />}
        {!isDone && (
          <button
            onClick={() => setIdx(STEP_IDS.length - 1)}
            className="text-xs text-[#4a4540] hover:text-[#8a8278] transition-colors"
          >
            skip
          </button>
        )}
      </div>

      {/* Content */}
      <div className="flex-1 flex flex-col">
        {step === "welcome"    && <WelcomeStep accent={accent} />}
        {step === "addressbar" && <AddressBarStep accent={accent} floatingBar={floatingBar} setFloatingBar={setFloatingBar} />}
        {step === "privacy"    && <PrivacyStep accent={accent} adBlock={adBlock} setAdBlock={setAdBlock} smartScreen={smartScreen} setSmartScreen={setSmartScreen} httpsForce={httpsForce} setHttpsForce={setHttpsForce} clearOnExit={clearOnExit} setClearOnExit={setClearOnExit} hibernate={hibernate} setHibernate={setHibernate} />}
        {step === "extensions" && <ExtensionsStep accent={accent} />}
        {step === "search"     && <SearchStep accent={accent} engine={engine} setEngine={setEngine} />}
        {step === "appearance" && <AppearanceStep accent={accent} setAccent={setAccent} theme={theme} setTheme={setTheme} importFrom={importFrom} setImportFrom={setImportFrom} importHistory={importHistory} setImportHistory={setImportHistory} importBookmarks={importBookmarks} setImportBookmarks={setImportBookmarks} setDefault={setDefault} setSetDefault={setSetDefault} />}
        {step === "done"       && <DoneStep accent={accent} />}
      </div>

      {/* Bottom nav */}
      {!isDone && (
        <div className="flex items-center justify-between px-8 py-5 border-t border-white/5">
          <button
            onClick={() => go(-1)}
            disabled={idx === 0}
            className="text-xs text-[#4a4540] hover:text-[#8a8278] disabled:opacity-0 disabled:pointer-events-none transition-colors flex items-center gap-1.5"
          >
            <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
              <path d="M9 2.5L4.5 7 9 11.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
            back
          </button>

          <span className="text-xs text-[#3a3630] font-mono tabular-nums">
            {String(idx + 1).padStart(2, "0")} / {String(total).padStart(2, "0")}
          </span>

          <button
            onClick={() => go(1)}
            className="flex items-center gap-2 px-5 py-2 rounded-lg text-sm font-medium transition-all hover:opacity-90 active:scale-[0.97]"
            style={{ background: accent, color: "#0e0d0b", marginTop: "40px" }}
          >
            {idx === total - 1 ? "Finish" : "Continue"}
            <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
              <path d="M5 2.5L9.5 7 5 11.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
          </button>
        </div>
      )}
    </div>
  );
}

// ─── Step: Welcome ────────────────────────────────────────────────────────────
function WelcomeStep({ accent }: { accent: string }) {
  return (
    <div className="flex-1 flex flex-col justify-center px-8 max-w-xl mx-auto w-full py-12">
      <p className="text-xs text-[#4a4540] uppercase tracking-widest mb-10 font-mono">First run</p>

      <h1 className="text-5xl font-semibold tracking-tight leading-tight mb-5 text-[#e8e2d7]">
        A browser that<br />
        works for you.
      </h1>

      <p className="text-[#6b6560] text-base leading-relaxed mb-12 max-w-sm">
        Stride is a light, customizable, and opinionated browser for Windows. Built to stay out of your way and do exactly what you configure it to do.
      </p>

      <div className="space-y-3">
        {[
          ["Light by design",       "Tab hibernation and sleep keep RAM and CPU low as your session grows"],
          ["uBlock Origin built in", "Ad and tracker blocking from the very first page load"],
          ["Focus mode",            "A hard lock that keeps you off distraction sites when you need to work"],
          ["YouTube tools",         "Quality control, speed presets, and hide anything you don't want to see"],
        ].map(([label, desc]) => (
          <div key={label} className="flex items-start gap-3.5">
            <div className="w-1.5 h-1.5 rounded-full mt-2 shrink-0" style={{ background: accent }} />
            <div>
              <span className="text-sm text-[#e8e2d7]">{label}. </span>
              <span className="text-sm text-[#4a4540]">{desc}.</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ─── Step: Address bar ────────────────────────────────────────────────────────
function AddressBarStep({ accent, floatingBar, setFloatingBar }: { accent: string; floatingBar: boolean; setFloatingBar: (v: boolean) => void }) {
  return (
    <div className="flex-1 flex flex-col justify-center px-8 max-w-2xl mx-auto w-full py-12">
      <p className="text-xs text-[#4a4540] uppercase tracking-widest mb-6 font-mono">Address bar</p>
      <h2 className="text-4xl font-semibold tracking-tight mb-3">How do you want to navigate?</h2>
      <p className="text-[#6b6560] text-sm mb-10 leading-relaxed max-w-md">
        Stride has two address bar modes. You can switch between them later in Settings. This just picks your starting point.
      </p>

      <div className="grid grid-cols-2 gap-4">
        {/* Floating */}
        <button
          onClick={() => setFloatingBar(true)}
          className="text-left rounded-2xl overflow-hidden transition-all"
          style={{
            border: `1.5px solid ${floatingBar ? accent : "#2a2824"}`,
            background: floatingBar ? `${accent}08` : "#161512",
          }}
        >
          {/* Mini preview */}
          <div className="bg-[#0e0d0b] border-b border-white/5 p-4 relative h-28 flex items-center justify-center">
            {/* Tab strip */}
            <div className="absolute top-2 left-3 flex gap-1">
              <div className="h-5 w-20 rounded-md flex items-center gap-1 px-2" style={{ background: "#1e1c18" }}>
                <div className="w-2 h-2 rounded-full shrink-0" style={{ background: accent }} />
                <div className="h-1.5 flex-1 rounded-full bg-white/10" />
              </div>
              <div className="h-5 w-4 rounded-md" style={{ background: "#1a1815" }} />
            </div>
            {/* Floating command bar */}
            <div
              className="absolute w-48 h-7 rounded-full flex items-center px-3 gap-2 top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 mt-2 shadow-lg"
              style={{ background: "#1e1c18", border: `1px solid ${accent}40` }}
            >
              <div className="w-2 h-2 rounded-full shrink-0" style={{ background: accent }} />
              <div className="h-1.5 flex-1 rounded-full" style={{ background: "#3a3630" }} />
            </div>
          </div>
          <div className="p-4">
            <div className="text-sm font-medium text-[#e8e2d7] mb-1">Floating command bar</div>
            <div className="text-xs text-[#6b6560] leading-relaxed">
              Centered overlay on top of the page. Toggle it with <kbd className="px-1 py-0.5 rounded text-[10px] font-mono bg-white/5">Ctrl+L</kbd>. Feels like a launcher.
            </div>
          </div>
        </button>

        {/* Standard */}
        <button
          onClick={() => setFloatingBar(false)}
          className="text-left rounded-2xl overflow-hidden transition-all"
          style={{
            border: `1.5px solid ${!floatingBar ? accent : "#2a2824"}`,
            background: !floatingBar ? `${accent}08` : "#161512",
          }}
        >
          {/* Mini preview */}
          <div className="bg-[#0e0d0b] border-b border-white/5 p-4 h-28 flex flex-col justify-start gap-2">
            {/* Toolbar with inline bar */}
            <div className="flex items-center gap-1.5">
              <div className="flex gap-1">
                {[0,1,2].map(i => <div key={i} className="w-4 h-4 rounded bg-white/5" />)}
              </div>
              <div
                className="flex-1 h-6 rounded-md flex items-center px-2 gap-1.5"
                style={{ background: "#1e1c18", border: `1px solid ${!floatingBar ? accent + "40" : "#2a2824"}` }}
              >
                <div className="w-1.5 h-1.5 rounded-full shrink-0" style={{ background: !floatingBar ? accent : "#3a3630" }} />
                <div className="h-1.5 flex-1 rounded-full bg-white/10" />
              </div>
              <div className="w-4 h-4 rounded bg-white/5" />
            </div>
            <div className="flex-1 rounded-md bg-white/[0.02] border border-white/5" />
          </div>
          <div className="p-4">
            <div className="text-sm font-medium text-[#e8e2d7] mb-1">Standard address bar</div>
            <div className="text-xs text-[#6b6560] leading-relaxed">
              Inline in the toolbar. Classic browser feel. Can be positioned left or right of the tab strip.
            </div>
          </div>
        </button>
      </div>

      <p className="text-xs text-[#3a3630] mt-5">
        Note: switching modes requires a restart.
      </p>
    </div>
  );
}

// ─── Step: Privacy ────────────────────────────────────────────────────────────
function PrivacyStep({ accent, adBlock, setAdBlock, smartScreen, setSmartScreen, httpsForce, setHttpsForce, clearOnExit, setClearOnExit, hibernate, setHibernate }: any) {
  return (
    <div className="flex-1 flex flex-col justify-center px-8 max-w-xl mx-auto w-full py-12">
      <p className="text-xs text-[#4a4540] uppercase tracking-widest mb-6 font-mono">Privacy</p>
      <h2 className="text-4xl font-semibold tracking-tight mb-3">Defaults that don't phone home.</h2>
      <p className="text-[#6b6560] text-sm mb-8 leading-relaxed max-w-md">
        Everything below is on unless you turn it off. Stride keeps your data on your machine.
      </p>

      <div className="rounded-2xl overflow-hidden" style={{ border: "1px solid #2a2824", background: "#111009" }}>
        <div className="px-5">
          <Row label="Ad & tracker blocking" sub="uBlock Origin v1.73.0, runs locally with no cloud dependency." right={<Toggle on={adBlock} onChange={setAdBlock} accent={accent} />} />
          <Row label="Force HTTPS" sub="Upgrades http:// to https:// automatically. Localhost stays http." right={<Toggle on={httpsForce} onChange={setHttpsForce} accent={accent} />} />
          <Row label="SmartScreen" sub="Checks URLs and downloads against reputation data. Requires restart to change." right={<Toggle on={smartScreen} onChange={setSmartScreen} accent={accent} />} />
          <Row label="Tab hibernation" sub="Unloads tabs after 1 to 5 minutes of inactivity based on open tab count." right={<Toggle on={hibernate} onChange={setHibernate} accent={accent} />} />
          <Row label="Clear data on exit" sub="Wipes history, cookies, and cache every time you close the window." right={<Toggle on={clearOnExit} onChange={setClearOnExit} accent={accent} />} />
        </div>
      </div>
    </div>
  );
}

// ─── Step: Extensions ────────────────────────────────────────────────────────
function ExtensionsStep({ accent }: { accent: string }) {
  const [lens, setLens] = useState(false);

  return (
    <div className="flex-1 flex flex-col justify-center px-8 max-w-xl mx-auto w-full py-12">
      <p className="text-xs text-[#4a4540] uppercase tracking-widest mb-6 font-mono">Extensions</p>
      <h2 className="text-4xl font-semibold tracking-tight mb-3">Two tools, already installed.</h2>
      <p className="text-[#6b6560] text-sm mb-8 leading-relaxed max-w-md">
        Stride ships with two built-in extensions. No extension store, no installs. They're always there.
      </p>

      <div className="space-y-3">
        {/* uBlock */}
        <div className="rounded-2xl p-5" style={{ border: "1px solid #2a2824", background: "#111009" }}>
          <div className="flex items-start justify-between gap-4 mb-3">
            <div>
              <div className="text-sm font-medium text-[#e8e2d7]">uBlock Origin</div>
              <div className="text-xs font-mono text-[#4a4540] mt-0.5">v1.73.0, verified SHA</div>
            </div>
            <div
              className="text-[10px] px-2 py-1 rounded-md font-mono uppercase tracking-wide shrink-0"
              style={{ background: `${accent}15`, color: accent }}
            >
              Always on
            </div>
          </div>
          <p className="text-xs text-[#6b6560] leading-relaxed">
            Blocks ads, trackers, and malware domains. The zip ships with Stride and is SHA-verified on load. It never downloads from the web after install. YouTube ads, banner ads, cookie banners: gone.
          </p>
        </div>

        {/* T&C Lens */}
        <div className="rounded-2xl p-5" style={{ border: "1px solid #2a2824", background: "#111009" }}>
          <div className="flex items-start justify-between gap-4 mb-3">
            <div>
              <div className="text-sm font-medium text-[#e8e2d7]">T&C Lens</div>
              <div className="text-xs font-mono text-[#4a4540] mt-0.5">Terms & Conditions reader</div>
            </div>
            <div
              className="text-[10px] px-2 py-1 rounded-md font-mono uppercase tracking-wide shrink-0"
              style={{ background: "#2a2824", color: "#6b6560" }}
            >
              On demand
            </div>
          </div>
          <p className="text-xs text-[#6b6560] leading-relaxed mb-4">
            Press <kbd className="px-1.5 py-0.5 rounded font-mono text-[10px] bg-white/5 text-[#8a8278]">Alt+T</kbd> on any page. Stride extracts the page text and opens it in a clean reader view so you can actually read what you're agreeing to.
          </p>

          {/* Mini demo */}
          <div
            className="rounded-xl overflow-hidden text-[10px] font-mono"
            style={{ border: "1px solid #1e1c18", background: "#0e0d0b" }}
          >
            <div className="flex items-center gap-2 px-3 py-2 border-b border-white/5">
              <div className="w-3 h-3 rounded-sm" style={{ background: accent }} />
              <span className="text-[#4a4540]">T&C Lens / some-service.com</span>
              <div className="ml-auto flex gap-1.5">
                <div className="h-3 w-12 rounded-sm bg-white/5" />
                <div className="h-3 w-16 rounded-sm" style={{ background: `${accent}20` }} />
              </div>
            </div>
            <div className="p-3 space-y-1.5">
              {["By using this service, you agree to...", "We may share your data with third parties...", "You waive your right to class action...", "We can change these terms at any time..."].map((l, i) => (
                <div key={i} className="h-2 rounded-full" style={{ background: i === 1 || i === 2 ? `${accent}30` : "#1e1c18", width: ["85%","78%","92%","70%"][i] }} />
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// ─── Step: Search ─────────────────────────────────────────────────────────────
function SearchStep({ accent, engine, setEngine }: { accent: string; engine: string; setEngine: (v: string) => void }) {
  const privacy = ["ddg","brave","start"];
  return (
    <div className="flex-1 flex flex-col justify-center px-8 max-w-xl mx-auto w-full py-12">
      <p className="text-xs text-[#4a4540] uppercase tracking-widest mb-6 font-mono">Search</p>
      <h2 className="text-4xl font-semibold tracking-tight mb-3">Who handles your queries?</h2>
      <p className="text-[#6b6560] text-sm mb-8 leading-relaxed max-w-md">
        Default is DuckDuckGo. Suggestions are fetched locally first and debounced at 150ms before any network request.
      </p>

      <div className="space-y-1.5">
        {ENGINES.map((e) => {
          const sel = engine === e.id;
          const isPrivate = privacy.includes(e.id);
          return (
            <button
              key={e.id}
              onClick={() => setEngine(e.id)}
              className="w-full text-left rounded-xl px-4 py-3.5 flex items-center gap-4 transition-all group"
              style={{
                background: sel ? `${accent}10` : "#111009",
                border: `1px solid ${sel ? accent + "50" : "#2a2824"}`,
              }}
            >
              <div
                className="w-4 h-4 rounded-full border-2 flex items-center justify-center shrink-0 transition-colors"
                style={{ borderColor: sel ? accent : "#3a3630", background: sel ? accent : "transparent" }}
              >
                {sel && <div className="w-1.5 h-1.5 rounded-full bg-[#0e0d0b]" />}
              </div>
              <div className="flex-1 min-w-0">
                <span className="text-sm text-[#e8e2d7]">{e.name}</span>
                <span className="text-xs text-[#4a4540] ml-2">{e.note}</span>
              </div>
              <span
                className="text-[10px] px-1.5 py-0.5 rounded font-mono shrink-0"
                style={{
                  background: isPrivate ? `${accent}12` : "#2a2824",
                  color: isPrivate ? accent : "#6b6560",
                }}
              >
                {e.tag}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

// ─── Step: Appearance + Import (combined) ─────────────────────────────────────
function AppearanceStep({ accent, setAccent, theme, setTheme, importFrom, setImportFrom, importHistory, setImportHistory, importBookmarks, setImportBookmarks, setDefault, setSetDefault }: any) {
  const themes = [
    { id: "system", label: "System" },
    { id: "light",  label: "Light" },
    { id: "dark",   label: "Dark" },
  ] as const;

  return (
    <div className="flex-1 flex flex-col justify-center px-8 max-w-xl mx-auto w-full py-12 overflow-y-auto">
      <p className="text-xs text-[#4a4540] uppercase tracking-widest mb-6 font-mono">Appearance & import</p>
      <h2 className="text-4xl font-semibold tracking-tight mb-8">Last things.</h2>

      {/* Theme */}
      <div className="mb-6">
        <div className="text-xs text-[#4a4540] mb-3 uppercase tracking-widest font-mono">Theme</div>
        <div className="flex gap-2">
          {themes.map(t => (
            <button
              key={t.id}
              onClick={() => setTheme(t.id)}
              className="flex-1 py-2.5 rounded-xl text-sm transition-all"
              style={{
                background: theme === t.id ? `${accent}15` : "#111009",
                border: `1px solid ${theme === t.id ? accent + "50" : "#2a2824"}`,
                color: theme === t.id ? "#e8e2d7" : "#4a4540",
              }}
            >
              {t.label}
            </button>
          ))}
        </div>
      </div>

      {/* Accent */}
      <div className="mb-8">
        <div className="text-xs text-[#4a4540] mb-3 uppercase tracking-widest font-mono">Accent color</div>
        <div className="flex gap-2.5">
          {ACCENTS.map(a => (
            <button
              key={a}
              onClick={() => setAccent(a)}
              title={a}
              className="w-8 h-8 rounded-full transition-all focus:outline-none"
              style={{
                background: a,
                boxShadow: accent === a ? `0 0 0 2px #0e0d0b, 0 0 0 3.5px ${a}` : "none",
                transform: accent === a ? "scale(1.15)" : "scale(1)",
              }}
            />
          ))}
        </div>
      </div>

      <div className="border-t border-white/5 pt-8 mb-6">
        <div className="text-xs text-[#4a4540] mb-3 uppercase tracking-widest font-mono">Import from</div>
        <div className="flex gap-2 mb-5">
          {(["chrome","edge","none"] as const).map(b => (
            <button
              key={b}
              onClick={() => setImportFrom(b)}
              className="flex-1 py-2.5 rounded-xl text-sm capitalize transition-all"
              style={{
                background: importFrom === b ? `${accent}15` : "#111009",
                border: `1px solid ${importFrom === b ? accent + "50" : "#2a2824"}`,
                color: importFrom === b ? "#e8e2d7" : "#4a4540",
              }}
            >
              {b === "none" ? "Skip" : b.charAt(0).toUpperCase() + b.slice(1)}
            </button>
          ))}
        </div>

        {importFrom !== "none" && (
          <div className="rounded-2xl overflow-hidden mb-5" style={{ border: "1px solid #2a2824", background: "#111009" }}>
            <div className="px-5">
              <Row label="Browsing history" sub="Last 90 days" right={<Checkbox on={importHistory} onChange={setImportHistory} accent={accent} />} />
              <Row label="Bookmarks" sub="All folders" right={<Checkbox on={importBookmarks} onChange={setImportBookmarks} accent={accent} />} />
            </div>
          </div>
        )}

        <div className="rounded-2xl overflow-hidden" style={{ border: "1px solid #2a2824", background: "#111009" }}>
          <div className="px-5">
            <Row label="Set Stride as default browser" sub="Links from other apps will open in Stride." right={<Toggle on={setDefault} onChange={setSetDefault} accent={accent} />} />
          </div>
        </div>
      </div>
    </div>
  );
}

// ─── Step: Done ───────────────────────────────────────────────────────────────
function DoneStep({ accent }: { accent: string }) {
  return (
    <div className="flex-1 flex flex-col items-center justify-center px-8 max-w-xl mx-auto w-full py-12">
      <div className="w-full mb-10 text-center">
        <img src={strideLogo} alt="Stride" className="w-14 h-14 rounded-2xl mb-8 mx-auto" />
        <h2 className="text-5xl font-semibold tracking-tight leading-tight mb-4 text-[#e8e2d7]">
          Ready.<br />
          Start browsing.
        </h2>
        <p className="text-[#6b6560] text-base leading-relaxed">
          84 features, all yours. Open Settings anytime to go deeper.
        </p>
      </div>

      <div className="w-full space-y-3 mb-14">
        {[
          ["New tab, close tab",       "Ctrl+T / Ctrl+W"],
          ["Focus address bar",        "Ctrl+L"],
          ["Open OneTab",              "Ctrl+Shift+O"],
          ["T&C Lens on current page", "Alt+T"],
          ["History",                  "Ctrl+H"],
          ["Settings",                 "Ctrl+,"],
        ].map(([desc, key]) => (
          <div key={key} className="flex items-center justify-between gap-4">
            <span className="text-sm text-[#6b6560]">{desc}</span>
            <kbd
              className="text-xs font-mono px-2.5 py-1 rounded-md text-center text-[#e8e2d7]"
              style={{ background: "#161512", border: "1px solid #2a2824", width: "9rem", display: "inline-block" }}
            >
              {key}
            </kbd>
          </div>
        ))}
      </div>

      <button
        className="w-full py-3.5 rounded-lg text-sm font-semibold transition-all hover:opacity-90 active:scale-[0.98]"
        style={{ background: accent, color: "#0e0d0b", marginTop: "40px" }}
      >
        Open new tab
      </button>
    </div>
  );
}
