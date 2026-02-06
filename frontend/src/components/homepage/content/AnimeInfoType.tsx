export type AnimeInfo = {
  bangumi_id: string;
  mikan_bangumi_id?: string;
  jp_title: string;
  ch_title?: string;
  en_title: string;
  ch_desc?: string;
  en_desc?: string;
  score: string;
  images: {
    portrait: string;
    landscape: string;
  };
  external_urls: {
    bangumi: string;
    tmdb: string;
    anilist: string;
  };
};