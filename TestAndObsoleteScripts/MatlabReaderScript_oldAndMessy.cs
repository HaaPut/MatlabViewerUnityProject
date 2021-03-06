﻿using UnityEngine;
using Accord.IO;
using UnityEngine.UI;
using UnityEngine.UI.Extensions;
using System.Collections.Generic;
using System.IO;

public class MatlabReaderScript : MonoBehaviour
{
    // UI components
    public Slider slider;
    public GameObject SliderCanvas;
    public AutoCompleteComboBox fileSelectionDropDown;
    private string[] _files;

    // prefabs
    [SerializeField] private GameObject _vertexColorMeshPrefab;
    [SerializeField] private GameObject _opaqueVertexColorPrefab;
    [SerializeField] private GameObject _singelColorMeshPrefab;
    [SerializeField] private GameObject _opaqueSingleColorPrefab;

    // mesh components
    [SerializeField] private Material _scatterMat;
    private GameObject meshInstance; 
    private GameObject _multicolorFV;
    private List<Color[]> vertexColorList;
    private Mesh _multicolorMesh;
    private Mesh _mesh;

    // graph components
    public GameObject graphPrefab; 
    public Transform graphContainer;
    private List<UILineRenderer> graphList;
    private List<double[,]> _graphXvalues;
    private List<double[,]> _graphYvalues;

    void Start()
    {
        slider.maxValue = 0;

        graphList = new List<UILineRenderer>();
        _graphXvalues = new List<double[,]>();
        _graphYvalues = new List<double[,]>();

        UpdateFileList();
        //UpdateMatlabFigure();
    }

    public void UpdateMatlabFigure()
    {
        DestroyAllChildren();
        SliderCanvas.SetActive(false);

        MatReader matFileReader = new MatReader(Application.streamingAssetsPath + Path.DirectorySeparatorChar + fileSelectionDropDown.Text);

        MatNode matFile = matFileReader.Fields[matFileReader.FieldNames[0]];

        foreach (var field in matFile.Fields)
        {
            MatNode _struct = matFile.Fields[field.Key];
            int[,] _type = _struct.Fields["type"].GetValue<int[,]>();

            switch (_type[0, 0])
            {
                case 1: // type 1 = FV triangulated mesh
                    FVmeshVertexColor(_struct);
                    break;
                case 2:
                    // type 2 = 3D scatter
                    scatter3(_struct);
                    break;
                case 3:
                    // multiple vertex color mesh
                    FVmeshMultiVertColor(_struct);
                    break;
                case 4:
                    // inset graph;
                    drawGraph(_struct);
                    break;
                // should add options for multigraph
                case 5:
                    // single color mesh
                    FVmeshSingleColor(_struct);
                    break;
                case 6:
                    // set camera distance
                    CamDistSetter(_struct);
                    break;

            }
        }
    }

    public void UpdateFileList()
    {
        _files = Directory.GetFiles(Application.streamingAssetsPath, "*.mat");

        List<string> _fileList = new List<string>();

        for (int i = 0; i < _files.Length; i++)
        {
            string[] temp = _files[i].Split(Path.DirectorySeparatorChar);
            _fileList.Add(temp[temp.Length - 1]);
        }

        fileSelectionDropDown.SetAvailableOptions(_fileList);
        fileSelectionDropDown.ItemsToDisplay = _fileList.Count;
    }


    private void FVmeshVertexColor(MatNode fv)
    {
        Vector3[] vertices = MatrixToVectorArray(fv.Fields["vertices"].GetValue<double[,]>());
        int[] faces = MatrixTo1DArray(fv.Fields["faces"].GetValue<int[,]>());

        double[,] col = fv.Fields["colors"].GetValue<double[,]>();

        _mesh = new Mesh();
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.vertices = vertices;
        _mesh.triangles = faces;
        _mesh.RecalculateNormals();

        UnityEngine.Gradient colMap = MatrixToColormap(fv.Fields["map"].GetValue<double[,]>());
        Color[] vertexColors = GetVertexColors(col, colMap, 0);
        _mesh.colors = vertexColors;

        double[,] temp = fv.Fields["opacity"].GetValue<double[,]>();
        float _opacity = (float)temp[0,0];

        if (_opacity < 1)
        {
            meshInstance = Instantiate(_vertexColorMeshPrefab, new Vector3(0, 0, 0), Quaternion.identity);
            meshInstance.GetComponent<Renderer>().material.SetFloat("_opacity", _opacity);
        } else
        {
            meshInstance = Instantiate(_opaqueVertexColorPrefab, new Vector3(0, 0, 0), Quaternion.identity);
        }

        meshInstance.transform.parent = transform;
        meshInstance.GetComponent<MeshFilter>().mesh = _mesh; //passing the mesh to the MeshFilteere so it can be displayed
        meshInstance.GetComponent<MeshCollider>().sharedMesh = _mesh; //adding _mesh to MeshCollider so it can be hit by raycasts (and do other collider physics)

    }

    private void FVmeshSingleColor(MatNode fv)
    {
        Vector3[] vertices = MatrixToVectorArray(fv.Fields["vertices"].GetValue<double[,]>());
        int[] faces = MatrixTo1DArray(fv.Fields["faces"].GetValue<int[,]>());

        Color color = GetColor(fv.Fields["color"].GetValue<double[,]>());

        _mesh = new Mesh();
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.vertices = vertices;
        _mesh.triangles = faces;
        _mesh.RecalculateNormals();

        double[,] temp = fv.Fields["opacity"].GetValue<double[,]>();
        float _opacity = (float)temp[0, 0];

        if (_opacity < 1f)
        {
            meshInstance = Instantiate(_singelColorMeshPrefab, new Vector3(0, 0, 0), Quaternion.identity);
            meshInstance.GetComponent<Renderer>().material.SetFloat("_opacity", _opacity);
        } else
        {
            meshInstance = Instantiate(_opaqueSingleColorPrefab, new Vector3(0, 0, 0), Quaternion.identity);
        }

        meshInstance.GetComponent<Renderer>().material.SetColor("_color", color);
        meshInstance.transform.parent = transform;
        meshInstance.GetComponent<MeshFilter>().mesh = _mesh;
        meshInstance.GetComponent<MeshCollider>().sharedMesh = _mesh; 
    }

    private void FVmeshMultiVertColor(MatNode fv)
    {
        SliderCanvas.SetActive(true);

        Vector3[] vertices = MatrixToVectorArray(fv.Fields["vertices"].GetValue<double[,]>());
        int[] faces = MatrixTo1DArray(fv.Fields["faces"].GetValue<int[,]>());

        double[,] col = fv.Fields["colors"].GetValue<double[,]>();

        _multicolorMesh = new Mesh();
        _multicolorMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _multicolorMesh.vertices = vertices;
        _multicolorMesh.triangles = faces;
        _multicolorMesh.RecalculateNormals();

        slider.maxValue = col.GetLength(1) - 1;
        slider.value = 0;

        UnityEngine.Gradient colMap = MatrixToColormap(fv.Fields["map"].GetValue<double[,]>());

        vertexColorList = new List<Color[]>();

        for (int i = 0; i < col.GetLength(1); i++)
        {
            vertexColorList.Add(GetVertexColors(col, colMap, i));
        }

        _multicolorMesh.colors = vertexColorList[0];

        double[,] temp = fv.Fields["opacity"].GetValue<double[,]>();
        float _opacity = (float)temp[0, 0];

        if (_opacity < 1)
        {
            _multicolorFV = Instantiate(_vertexColorMeshPrefab, new Vector3(0, 0, 0), Quaternion.identity);
            _multicolorFV.GetComponent<Renderer>().material.SetFloat("_opacity", _opacity);
        } else
        {
            _multicolorFV = Instantiate(_opaqueVertexColorPrefab, new Vector3(0, 0, 0), Quaternion.identity);
        }

        _multicolorFV.transform.parent = transform;
        _multicolorFV.GetComponent<MeshFilter>().mesh = _multicolorMesh;
        _multicolorFV.GetComponent<MeshCollider>().sharedMesh = _multicolorMesh;
    }

    public void UpdateVertexColors()
    {
        int tab = (int)slider.value;

        _multicolorMesh.colors = vertexColorList[tab];
        _multicolorFV.GetComponent<MeshFilter>().mesh = _multicolorMesh;
    }

    private void scatter3(MatNode sc)
    {
        GameObject scatterInstance = new GameObject("scatter3Container");
        scatterInstance.transform.parent = transform;

        Vector3[] pts = MatrixToVectorArray(sc.Fields["pts"].GetValue<double[,]>());
        int[,] sz = sc.Fields["size"].GetValue<int[,]>();

        Color color = GetColor(sc.Fields["color"].GetValue<double[,]>());

        for (int i = 0; i < pts.GetLength(0); i++)
        {
            GameObject sp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sp.transform.position = pts[i];
            sp.transform.localScale = new Vector3(sz[0, i], sz[0, i], sz[0, i]);
            sp.GetComponent<Renderer>().material = _scatterMat;
            //sp.GetComponent<Renderer>().material.shader = Shader.Find("HDRP/Lit");
            sp.GetComponent<Renderer>().material.SetColor("_BaseColor", color);
            sp.transform.parent = scatterInstance.transform;
        }
    }

    private void drawGraph(MatNode gr)
    {
        GameObject _graph = Instantiate(graphPrefab, new Vector3(0, 0, 0), Quaternion.identity);
        _graph.transform.SetParent(graphContainer, false);
        UILineRenderer uILineRenderer = _graph.GetComponent <UILineRenderer>();

        Color color = GetColor(gr.Fields["color"].GetValue<double[,]>());
        double[,] x = gr.Fields["x"].GetValue<double[,]>();
        double[,] y = gr.Fields["y"].GetValue<double[,]>();

        Vector2[] points = buildPointsArray(x, y, 0);

        uILineRenderer.color = color;
        uILineRenderer.Points = points;

        if (y.GetLength(1) > 1) // if graph has more than 1 set of y values it becomes scrollable
        {
            graphList.Add(uILineRenderer);
            _graphXvalues.Add(x);
            _graphYvalues.Add(y);
        }
    }

    public void UpdateGraph()
    {
        int idx = (int)slider.value;

        for (int i = 0; i < graphList.Count; i++)
        {
            Vector2[] points = buildPointsArray(_graphXvalues[i], _graphYvalues[i], idx);
            graphList[i].Points = points; 
        }
    }

    private void CamDistSetter(MatNode d)
    {
        int[,] _dist = d.Fields["CamDistance"].GetValue<int[,]>();
        CamOrbit.functions.SetCamDistance(_dist[0,0]);
    }

    // data reader functions
    private Vector3[] MatrixToVectorArray(double[,] matrix)
    {
        int rowCount = matrix.GetLength(0);
        Vector3[] arr = new Vector3[rowCount];
        Vector3 temp = new Vector3(0, 0, 0);
        for (int i = 0; i < rowCount; i++)
        {
            temp.x = -1*(float)matrix[i, 0];    //matlab and Unity are using different axis handedness
            temp.y = (float)matrix[i, 1];
            temp.z = (float)matrix[i, 2];
            arr[i] = temp;
        }
        return (arr);
    }

    private int[] MatrixTo1DArray(int[,] matrix)
    {
        int[] arr = new int[matrix.Length];
        Vector3 temp = new Vector3(0, 0, 0);
        for (int i = 0; i < matrix.GetLength(0); i++)
        {
            arr[i * 3] = matrix[i, 0];
            arr[i * 3 + 1] = matrix[i, 1];
            arr[i * 3 + 2] = matrix[i, 2];
        }
        return (arr);
    }

    private UnityEngine.Gradient MatrixToColormap(double[,] matrix)
    {
        UnityEngine.Gradient map = new UnityEngine.Gradient();

        int rowCount = matrix.GetLength(0);
        GradientColorKey[] colorKey = new GradientColorKey[rowCount];

        Color temp = new Color();

        for (int i = 0; i < rowCount; i++)
        {
            temp.r = (float)matrix[i, 0];
            temp.g = (float)matrix[i, 1];
            temp.b = (float)matrix[i, 2];

            colorKey[i].color = temp;
            colorKey[i].time = (float)i / rowCount;
        }

        GradientAlphaKey[] alphaKey = new GradientAlphaKey[2];
        alphaKey[0].alpha = 1.0f;
        alphaKey[0].time = 0.0f;
        alphaKey[1].alpha = 0.0f;
        alphaKey[1].time = 1.0f;

        map.SetKeys(colorKey, alphaKey);

        return (map);
    }

    private Color[] GetVertexColors(double[,] vertCol, UnityEngine.Gradient gradient, int idx)
    {
        Color[] colors = new Color[vertCol.GetLength(0)];

        for (int i = 0; i < vertCol.GetLength(0); i++)
        {
            colors[i] = gradient.Evaluate((float)vertCol[i, idx]);
        }
        return (colors);
    }

    private Color GetColor(double[,] col)
    {
        Color _color = new Color(0, 0, 0);
        _color.r = (float)col[0, 0];
        _color.g = (float)col[0, 1];
        _color.b = (float)col[0, 2];

        return _color;
    }

    // build and destroy functions
    private void DestroyAllChildren()
    {
        foreach (Transform child in transform)
        {
            GameObject.Destroy(child.gameObject);
        }

        foreach (Transform child in graphContainer.transform)
        {
            GameObject.Destroy(child.gameObject);
        }

        graphList.Clear();
        _graphXvalues.Clear();
        _graphYvalues.Clear();
    }

    private Vector2[] buildPointsArray(double[,] x, double[,] y, int idx)
    {
        Vector2[] pointsArray = new Vector2[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            pointsArray[i].x = (float)x[i, 0];
            pointsArray[i].y = (float)y[i, idx];
        }
        return pointsArray;
    }
}
