using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;

// The single-backend footprint probe: the project references all backend assemblies (like a
// cross-platform app would), but the code touches only the GTK one — so a trimmed or AOT publish
// must link GTK alone and drop the Windows and macOS backends without a trace. CI asserts exactly
// that on the publish output.
BackendRegistry.Register(new GtkBackend());

var form = new Form { Text = "TrimProbe", Bounds = new(100, 100, 320, 200) };
form.Controls.Add(new Label { Text = "Only the GTK backend is linked.", Bounds = new(10, 10, 300, 20) });
Application.Run(form);
