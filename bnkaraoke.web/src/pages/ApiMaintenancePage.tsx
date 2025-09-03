import React, { useEffect, useState, useCallback } from "react";
import { API_ROUTES } from "../config/apiConfig";

interface ApiSetting {
  id: number;
  settingKey: string;
  settingValue: string;
}

interface MatureSong {
  id: number;
  title: string;
  artist: string;
  youTubeUrl: string;
}

const ApiMaintenancePage: React.FC = () => {
  const [settings, setSettings] = useState<ApiSetting[]>([]);
  const [newKey, setNewKey] = useState("");
  const [newValue, setNewValue] = useState("");
  const [status, setStatus] = useState<string>("");
  const [matureSongs, setMatureSongs] = useState<MatureSong[]>([]);

  const token = localStorage.getItem("token") || "";

  const fetchSettings = useCallback(async () => {
    const res = await fetch(API_ROUTES.API_SETTINGS, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (res.ok) {
      const data = await res.json();
      setSettings(data);
    }
  }, [token]);

  const fetchMatureSongs = useCallback(async () => {
    const res = await fetch(API_ROUTES.API_MATURE_NOT_CACHED, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (res.ok) {
      const data = await res.json();
      setMatureSongs(data);
    }
  }, [token]);

  useEffect(() => {
    fetchSettings();
    fetchMatureSongs();
  }, [fetchSettings, fetchMatureSongs]);

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
    setStatus("Resyncing cache...");
    await fetch(API_ROUTES.API_RESYNC_CACHE, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
    });

    setStatus("Starting manual cache...");
    const res = await fetch(API_ROUTES.API_MANUAL_CACHE_START, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
    });

    if (res.ok) {
      const statusRes = await fetch(API_ROUTES.API_MANUAL_CACHE_STATUS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const data = await statusRes.json();
      setStatus(`Manual cache: ${data.processed} of ${data.total} files to be cached`);
    }
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
    setStatus(`Manual cache: ${data.processed} of ${data.total} files to be cached`);
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
      <h4>Mature Songs Not Cached</h4>
      <button onClick={fetchMatureSongs}>Refresh</button>
      <table>
        <thead>
          <tr>
            <th>ID</th>
            <th>Title</th>
            <th>Artist</th>
            <th>YouTube URL</th>
          </tr>
        </thead>
        <tbody>
          {matureSongs.map((s) => (
            <tr key={s.id}>
              <td>{s.id}</td>
              <td>{s.title}</td>
              <td>{s.artist}</td>
              <td>
                <a href={s.youTubeUrl} target="_blank" rel="noopener noreferrer">
                  {s.youTubeUrl}
                </a>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default ApiMaintenancePage;
