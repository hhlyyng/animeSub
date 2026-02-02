import  { useState } from "react";
import type { AnimeInfo } from "./AnimeInfoType";

type AnimeCardProps = {
  anime: AnimeInfo;
};


export function AnimeCard( { anime }: AnimeCardProps) {
  const [open, setOpen] = useState(false);
  const [isHovered, setIsHovered] = useState(false);

  return (
    <div
      className="
                relative cursor-pointer select-none
                transition-transform duration-300
                "
      onClick={()=> {setOpen(true)}}
      onMouseEnter={()=>setIsHovered(true)}
      onMouseLeave={()=>setIsHovered(false)}
    >
      {/* Cover */}
      <div 
        className="relative overflow-hidden bg-gray-100 transition-all duration-300 ease-in-out"
        style={{
          height: '300px', // 固定高度
          width: isHovered && anime.images.landscape 
            ? '534px'  
            : '200px'  
        }}
      >
        <img
          src={isHovered && anime.images.landscape 
               ? anime.images.landscape 
               : anime.images.portrait}
          alt={anime.ch_title}
          className="absolute inset-0 h-full w-full object-cover transition-opacity duration-300"
        />
      </div>

      {/* title + score*/}
      <div className="mt-2 text-sm font-semibold text-gray-900 break-all"
            style={{ width: '200px' }}>
        {anime.ch_title}
      </div>
    </div>
  );
}

export default AnimeCard;
