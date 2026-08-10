namespace AdminWeb.Shared.Enums;

/// <summary>De qué va la publicación. Sirve para filtrar y para que el muro no sea una sopa.</summary>
public enum ForumTopic
{
    Idea = 0,
    Pregunta = 1,
    /// <summary>Algo que alguien aprendió y vale la pena que no se pierda.</summary>
    Aprendizaje = 2,
    Anuncio = 3,
    Otro = 4
}
