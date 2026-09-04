namespace LessonDisplay.Server.Licensing;

/// <summary>
/// Self-contained HTML shown instead of the normal admin/display pages once
/// the trial has run out. Deliberately independent of style.css/admin.js —
/// it needs to render correctly on its own even if something about the
/// normal app is misconfigured.
/// </summary>
public static class LicenseGatePage
{
    public static string Render() => """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>ClassSync — Trial Expired</title>
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <style>
          body { font-family: "Segoe UI", Arial, sans-serif; background: #1F2937; color: #fff;
                 display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; }
          .card { background: #fff; color: #111827; border-radius: 12px; padding: 32px 36px;
                   max-width: 420px; width: 90%; box-shadow: 0 10px 40px rgba(0,0,0,0.3); }
          h1 { font-size: 20px; margin: 0 0 10px; }
          p { font-size: 14px; line-height: 1.5; color: #4B5563; }
          input { width: 100%; box-sizing: border-box; padding: 10px 12px; border: 1px solid #D1D5DB;
                  border-radius: 7px; font-size: 14px; margin-top: 6px; font-family: monospace; }
          button { margin-top: 14px; width: 100%; padding: 11px; border: none; border-radius: 8px;
                    background: #4338CA; color: #fff; font-weight: 700; font-size: 14px; cursor: pointer; }
          button:disabled { opacity: 0.6; cursor: default; }
          .msg { font-size: 13px; font-weight: 600; margin-top: 12px; padding: 8px 10px; border-radius: 7px; display: none; }
          .msg.err { display: block; background: #FEE2E2; color: #991B1B; }
          .msg.ok { display: block; background: #DCFCE7; color: #166534; }
        </style>
        </head>
        <body>
          <div class="card">
            <h1>Your 7-day trial has ended</h1>
            <p>Enter your license key below to keep using ClassSync. If you haven't purchased one yet, contact the seller you got this from.</p>
            <input type="text" id="key" placeholder="CSN1....">
            <button id="activateBtn" type="button">Activate</button>
            <div class="msg" id="msg"></div>
          </div>
          <script>
            document.getElementById("activateBtn").addEventListener("click", function () {
              var btn = this, msg = document.getElementById("msg"), key = document.getElementById("key").value.trim();
              if (!key) return;
              btn.disabled = true;
              fetch("/api/license/activate", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ key: key }),
              })
                .then(function (r) { return r.json().then(function (body) { return { ok: r.ok, body: body }; }); })
                .then(function (result) {
                  if (result.ok) {
                    msg.className = "msg ok";
                    msg.textContent = "License activated — reloading...";
                    setTimeout(function () { window.location.reload(); }, 800);
                  } else {
                    msg.className = "msg err";
                    msg.textContent = result.body.error || "Could not activate this key.";
                    btn.disabled = false;
                  }
                })
                .catch(function () {
                  msg.className = "msg err";
                  msg.textContent = "Could not reach the server.";
                  btn.disabled = false;
                });
            });
          </script>
        </body>
        </html>
        """;
}
