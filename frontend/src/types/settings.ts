export type SettingsTokenStatus = {
  configured: boolean;
  preview: string | null;
};

export type QbittorrentSettings = {
  host: string;
  port: number;
  username: string;
  passwordConfigured: boolean;
  defaultSavePath: string;
  category: string;
  tags: string;
};

export type MikanSettings = {
  pollingIntervalMinutes: number;
};

export type AnimeSubSettings = {
  username: string;
  passwordConfigured: boolean;
};

export type DownloadPreferenceField = "subgroup" | "resolution" | "subtitleType";

export type DownloadPreferencesSettings = {
  subgroup: string;
  resolution: string;
  subtitleType: string;
  priorityOrder: DownloadPreferenceField[];
};

export type DownloadPreferenceOptions = {
  subgroups: string[];
  resolutions: string[];
  subtitleTypes: string[];
  priorityFields: DownloadPreferenceField[];
};

export type SettingsProfile = {
  tmdb: SettingsTokenStatus;
  qbittorrent: QbittorrentSettings;
  animeSub?: AnimeSubSettings;
  mikan: MikanSettings;
  downloadPreferences: DownloadPreferencesSettings;
  downloadPreferenceOptions: DownloadPreferenceOptions;
};

export type UpdateSettingsProfileRequest = {
  tmdbToken?: string | null;
  qbittorrent: {
    host: string;
    port: number;
    username?: string | null;
    password?: string | null;
    defaultSavePath: string;
    category?: string | null;
    tags?: string | null;
  };
  animeSub: {
    username?: string | null;
    password?: string | null;
  };
  mikan: {
    pollingIntervalMinutes?: number | null;
  };
  downloadPreferences: {
    subgroup: string;
    resolution: string;
    subtitleType: string;
    priorityOrder: DownloadPreferenceField[];
  };
};

export type SettingsTestResponse = {
  success: boolean;
  message: string;
};

export type TestQbittorrentRequest = {
  field: "host" | "port" | "username" | "password" | "defaultSavePath" | "category" | "tags";
  host: string;
  port: number;
  username?: string;
  password?: string;
  defaultSavePath: string;
  category?: string;
  tags?: string;
};
