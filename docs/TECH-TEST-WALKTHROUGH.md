# Phoenix LAI: Tech Test Walkthrough

This guide tests Phoenix LAI end to end on one PC: the **Local LLM** (Gemma 4 on your GPU) and a **Cloud API** (any OpenAI-compatible `/v1` provider). It takes about 30 to 45 minutes.

![How the pieces connect](walkthrough/01-architecture.svg)

---

## 1. Before you start

| You need | Notes |
|---|---|
| Windows 10 or 11 with PowerShell 5.1 | Built in. Do not use PowerShell 7. |
| A GPU driver with Vulkan | Usually installed with the driver. |
| The Phoenix LAI folder | Downloaded from the repo or a zip you were given. |
| The Gemma model file | `models\gemma-4-12b-it-qat-q4_0.gguf`. |
| A cloud API base URL and key | From your provider. Keep the key private. |
| Chrome or Microsoft Edge | The chat opens in an app window. |

> **Privacy:** the cloud key is saved in Windows Credential Manager, never in a file. Use a test key with a spending limit where you can.

---

## 2. Start Phoenix

1. Double-click **`Phoenix_LAI.bat`** in the Phoenix folder.
   - First run creates the two shortcuts (`Phoenix_Client.lnk` and `Phoenix_LAI_Server.lnk`). This is normal.
2. Phoenix starts the **server** (models) and the **client** (chat window).
3. Wait until the chat window opens.

If you only need the server later, double-click **`Phoenix_LAI_Server.lnk`**. Use **`Phoenix_Client.lnk`** for the chat.

![Test flow](walkthrough/02-test-flow.svg)

---

## 3. Test the Local LLM

### 3.1 Check the server
1. Open the **Providers** list at the bottom of the left panel.
2. Find **Phoenix Local Server** (`http://localhost:1234/v1`).
3. Its dot should be **green**.

![Status colours](walkthrough/03-status-legend.svg)

**Expected:** green. **If red:** the server is not running. Start **`Phoenix_LAI_Server.lnk`** and wait about a minute while the model loads.

### 3.2 Pick the local model
1. Open the model menu at the top of the chat.
2. Choose a **Phoenix Local Server** model (Gemma 4).

### 3.3 Send a first message
Type exactly this and press **Enter**:

```text
In one sentence, what is a GPU?
```

**Expected:**
- The reply streams in word by word.
- The provider dot turns **blue** while it works, then back to **green**.
- No red error line appears.

### 3.4 Test a file action (optional, local only)
1. Create a test folder: `%USERPROFILE%\Phoenix_Projects\tech-test`.
2. In Phoenix, type `/folder /tech-test`.
3. Type: `List the files in this project.`

**Expected:** the model lists the files. Local is the only provider that can read project files.

---

## 4. Add a Cloud API

### 4.1 Open the add window
1. In the **Providers** list, click the **+** button.

### 4.2 Fill in the fields

| Field | What to enter | Example |
|---|---|---|
| **Name** | Any label you like | `Cloud test` |
| **Base URL** | The provider's OpenAI `/v1` address | `https://api.your-provider.com/v1` |
| **API key** | Your key | Pasted once, then saved to Credential Manager |
| **Send local context** | Leave **off** | Cloud never gets your files |

> The base URL must end in `/v1` and use `https://`. Phoenix refuses redirects and needs TLS 1.2 or newer.

### 4.3 Save and test
1. Press **Save**.
2. Press **Test**. It checks 18 OpenAI features and lists what the provider supports.
3. Models load from the provider's `/models` list into the model menu.

**Expected:** the new provider dot is green, and its models appear in the model menu.

![Cloud add flow](walkthrough/02-test-flow.svg)

---

## 5. Chat with the Cloud API

1. Open the model menu and choose a model from **Cloud test**.
2. Send the same message as before:

```text
In one sentence, what is a GPU?
```

**Expected:**
- The reply comes from the cloud model. The top bar shows the cloud model name.
- No red error line appears.

Then switch back to **Phoenix Local Server**, send the same message, and confirm the top bar changes again.

---

## 6. Failure checks

| Symptom | Likely cause | Fix |
|---|---|---|
| Local dot is **red** | Server not running | Start `Phoenix_LAI_Server.lnk`, wait for the model to load |
| Cloud dot is **red** | Wrong base URL, or no `/v1` | Edit the provider and check the URL |
| `401` or "unauthorized" | Key wrong or expired | Edit the provider and paste the key again |
| `404` or "model not found" | Model name not offered by provider | Use a model from the Test result |
| Cloud stalls, then fails | Firewall or proxy blocks the host | Ask IT to allow the provider's host |
| Reply is blank or cut off | Context too long | Type `/new` and try again |

---

## 7. Checklist

Copy this into your test record.

- [ ] Phoenix started, client window opened
- [ ] Phoenix Local Server is **green**
- [ ] Local model answered the test message
- [ ] Local answer streamed without a red error line
- [ ] Cloud provider added with name, `/v1` base URL and key
- [ ] Cloud **Test** completed
- [ ] Cloud model appears in the model menu
- [ ] Cloud model answered the test message
- [ ] Switching between Local and Cloud changes the top bar
- [ ] Any failure recorded with the exact error text

**Tester:** ____________________ **Date:** ____________ **Result:** Pass / Fail

---

## 8. Reporting a problem

Send the tester's notes with:

1. The step number and the checklist item that failed.
2. The **exact** error text from the red line.
3. Which provider was selected (Local or Cloud name).
4. Whether `/debug on` was enabled (output is in `%LOCALAPPDATA%\LocalLLMStudio\debug.txt`).

Do **not** send API keys or your `providers.json` file.
