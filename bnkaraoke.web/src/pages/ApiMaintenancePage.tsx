import React, { useEffect, useState } from "react";
import { API_ROUTES } from "../config/apiConfig";

interface ApiSetting {
  id: number;
  settingKey: string;
  settingValue: string;
}

const ApiMaintenancePage: React.FC = () => {
  const [settings, setSettings] = useState<ApiSetting[]>([]);
  const [newKey, setNewKey] = useState("");
  const [newValue, setNewValue] = useState("");
  const [status, setStatus] = useState<string>("");

  const token = localStorage.getItem("token") || "";

  const fetchSettings = async () => {
    const res = await fetch(API_ROUTES.API_SETTINGS, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (res.ok) {
      const data = await res.json();
      setSettings(data);
    }
  };

  useEffect(() => {
    fetchSettings();
  }, []);

  const addSetting = async () => {
    await fetch(API_ROUTES.API_SETTINGS, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ settingKey: newKey, settingValue: newValue }),
    });
    setNewKey("");
    setNewValue("");
    fetchSettings();
  };

  const updateSetting = async (setting: ApiSetting) => {
    await fetch(`${API_ROUTES.API_SETTINGS}/${setting.id}`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify(setting),
    });
    fetchSettings();
  };

  const deleteSetting = async (id: number) => {
    await fetch(`${API_ROUTES.API_SETTINGS}/${id}`, {
      method: "DELETE",
      headers: { Authorization: `Bearer ${token}` },
    });
    fetchSettings();
  };

  const resyncCache = async () => {
    setStatus("Resyncing cache...");
    const res = await fetch(API_ROUTES.API_RESYNC_CACHE, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
    });
    const data = await res.json();
    setStatus(`Resynced: ${data.updated} songs updated`);
  };

  const startManual = async () => {
    setStatus("Starting manual cache...");
    await fetch(API_ROUTES.API_MANUAL_CACHE_START, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
    });
  };

  const stopManual = async () => {
    await fetch(API_ROUTES.API_MANUAL_CACHE_STOP, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
    });
  };

  const checkStatus = async () => {
    const res = await fetch(API_ROUTES.API_MANUAL_CACHE_STATUS, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const data = await res.json();
    setStatus(`Manual cache: ${data.processed}/${data.total} (${data.percent.toFixed(1)}%)`);
  };

  return (
    <div style={{ padding: "1rem" }}>
      <h2>API Maintenance</h2>
      <h3>API Settings</h3>
      <table>
        <thead>
          <tr>
            <th>Key</th>
            <th>Value</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {settings.map((s) => (
            <tr key={s.id}>
              <td>
                <input
                  value={s.settingKey}
                  onChange={(e) => setSettings(settings.map(x => x.id === s.id ? { ...x, settingKey: e.target.value } : x))}
                />
              </td>
              <td>
                <input
                  value={s.settingValue}
                  onChange={(e) => setSettings(settings.map(x => x.id === s.id ? { ...x, settingValue: e.target.value } : x))}
                />
              </td>
              <td>
                <button onClick={() => updateSetting(s)}>Save</button>
                <button onClick={() => deleteSetting(s.id)}>Delete</button>
              </td>
            </tr>
          ))}
          <tr>
            <td>
              <input value={newKey} onChange={(e) => setNewKey(e.target.value)} />
            </td>
            <td>
              <input value={newValue} onChange={(e) => setNewValue(e.target.value)} />
            </td>
            <td>
              <button onClick={addSetting}>Add</button>
            </td>
          </tr>
        </tbody>
      </table>

      <h3>Cache Tools</h3>
      <button onClick={resyncCache}>Resync Cache</button>
      <button onClick={startManual}>Start Manual Cache</button>
      <button onClick={stopManual}>Stop Manual Cache</button>
      <button onClick={checkStatus}>Check Status</button>
      <p>{status}</p>
    </div>
  );
};

export default ApiMaintenancePage;
