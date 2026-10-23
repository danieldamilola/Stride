export const meta = {
  name: "dev-cleanup",
  description: "Clean generated build output across C:\\dev",
  phases: [
    { name: "scan", agent: "explore" },
    { name: "delete", agent: "general" }
  ]
};

const ROOT = "C:\\dev";
const GENERATED_DIRS = [
  "bin", "obj", "publish", "TestResults", "testresults",
  "target", "__pycache__", ".pytest_cache", ".mypy_cache",
  ".next", ".turbo", "coverage", ".dart_tool"
];
const SKIP_PATHS = [
  "\\.tools\\flutter",
  "\\.tools\\flutter-sdk",
  "node_modules"
];

async function scan({ agent }) {
  const script = `
    $script:results = @()
    function Scan($path, $depth) {
      if ($depth -gt 5) { return }
      Get-ChildItem $path -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $n = $_.Name
        if ($n -eq '.git' -or $n -eq 'node_modules') { return }
        if ("${GENERATED_DIRS.join('","')}" -contains $n) {
          $size = (Get-ChildItem $_.FullName -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
          if ($size -gt 0) {
            $script:results += @{ Path = $_.FullName; Name = $n; MB = [math]::Round($size/1MB, 1) }
          }
        } else {
          Scan $_.FullName ($depth + 1)
        }
      }
    }
    Scan "${ROOT}" 0
    $script:results | ConvertTo-Json -Depth 3
  `;
  const result = await agent({ task: script });
  return JSON.parse(result);
}

async function delete({ agent }, targets) {
  const script = `
    $total = 0
    foreach ($t in ${JSON.stringify(targets)}) {
      if (Test-Path -LiteralPath $t) {
        $size = (Get-ChildItem -LiteralPath $t -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        $total += $size
        Write-Output "deleting $t ($([math]::Round($size/1MB,1)) MB)"
        Remove-Item -LiteralPath $t -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
    Write-Output "FREED_MB=$([math]::Round($total/1MB,1))"
  `;
  return agent({ task: script });
}

export async function main({ args = {} }) {
  const dryRun = args.dryRun ?? false;
  const includeNodeModules = args.includeNodeModules ?? false;

  const scanAgent = agent({ name: "scan" });
  const items = await scan({ agent: scanAgent });
  
  const targets = items
    .filter(i => !SKIP_PATHS.some(s => i.Path.includes(s)))
    .map(i => i.Path);
  
  if (includeNodeModules) {
    // add node_modules scan
  }

  console.log(`Found ${targets.length} folders, ${targets.reduce((sum, t) => sum + items.find(i => i.Path === t).MB, 0).toFixed(1)} MB`);

  if (dryRun) {
    console.log("DRY RUN - would delete:", targets);
    return { dryRun: true, targets, totalMB: items.reduce((sum, i) => sum + i.MB, 0) };
  }

  const deleteAgent = agent({ name: "delete" });
  const result = await delete({ agent: deleteAgent }, targets);
  console.log(result);
  return { deleted: targets.length, ...result };
}