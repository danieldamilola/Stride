
$content = Get-Content MainWindow.xaml.cs -Raw
$content = $content -replace "using StrideBrowser.ViewModels;", "using StrideBrowser.ViewModels;`r`nusing StrideBrowser.ViewModels.Reader;`r`nusing StrideBrowser.Services.Reader;"
Set-Content MainWindow.xaml.cs $content

