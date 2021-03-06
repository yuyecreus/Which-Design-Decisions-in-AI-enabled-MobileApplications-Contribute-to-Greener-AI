using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using UnityEngine.UI;
using System;
using System.IO;


public class ModelManager : MonoBehaviour
{
	public AppManager app;

    public Text accuracytext;

    public NNModel modelSource;

    private IWorker worker;
    private Model model;

    public RawImage seeingImg;
    public Texture2D trial_texture;

   

    // Start is called before the first frame update
    void Start()
    {
        /*
        foreach (var layer in model.layers)
            Debug.Log(layer.name + " does " + layer.type);

        */
        

    }

    // Update is called once per frame
    void Update()
    {
    }


    public void StartModel()
    {
        if (worker == null)
		{
			model = ModelLoader.Load(modelSource, verbose: false);
			worker = WorkerFactory.CreateWorker(model, verbose: false);
            Debug.Log(model);
		}

    }

    public void KillModel()
    {
        if (worker != null)
        {
            worker.Dispose();
            worker = null;
        }

    }

    public Texture2D rotate270(Texture2D orig)
    {
        Color32[] origpix = orig.GetPixels32(0);
        Color32[] newpix = new Color32[orig.width * orig.height];
        int i = 0;
        for (int c = 0; c < orig.height; c++)
        {
            for (int r = 0; r < orig.width; r++)
            {
                newpix[orig.width * orig.height - (orig.height * r + orig.height) + c] = origpix[i];
                i++;
            }
        }
        Texture2D newtex = new Texture2D(orig.height, orig.width, orig.format, false);
        newtex.SetPixels32(newpix, 0);
        newtex.Apply();
        return newtex;
    }


    private Texture2D rotate90(Texture2D orig)
    {
        Color32[] origpix = orig.GetPixels32(0);
        Color32[] newpix = new Color32[orig.width * orig.height];
        for (int c = 0; c < orig.height; c++)
        {
            for (int r = 0; r < orig.width; r++)
            {
                newpix[orig.width * orig.height - (orig.height * r + orig.height) + c] =
                  origpix[orig.width * orig.height - (orig.width * c + orig.width) + r];
            }
        }
        Texture2D newtex = new Texture2D(orig.height, orig.width, orig.format, false);
        newtex.SetPixels32(newpix, 0);
        newtex.Apply();
        return newtex;
    }


    Texture2D Resize(Texture2D texture2D, int targetX, int targetY)
    {

        RenderTexture rt = new RenderTexture(targetX, targetY,1);
        RenderTexture.active = rt;
        Graphics.Blit(texture2D, rt);
        Texture2D result = new Texture2D(targetX, targetY);
        result.ReadPixels(new Rect(0,0, targetX, targetY), 0, 0);
        result.Apply();

        Texture2D rot_result = rotate270(result);
        

        return rot_result;
    }

    Tensor CropPytorch(Tensor source, int targetWidth, int targetHeight)
    {
        int sourceWidth = source.width;
        int sourceHeight = source.height;


        int crop_top = (int)((sourceHeight - targetHeight + 1) * 0.5);

        int crop_left = (int)((sourceWidth - targetWidth + 1) * 0.5);


        var cropped_img = new Tensor(1, targetHeight, targetWidth, 3);
        for (int channel = 0; channel < 3; channel++) {
            for (int i = crop_top; i < sourceHeight- crop_top; i++)
            {
                for (int j = crop_left; j < sourceWidth - crop_top; j++)
                {
                    cropped_img[0,i-crop_top, j-crop_left, channel] = source[0,i, j, channel];
                }
            }
        }
        Debug.Log("SHAPE: " + cropped_img.shape.ToString());
        return cropped_img;
    }


    Texture2D ResampleAndCrop(Texture2D source, int targetWidth, int targetHeight)
    {
        int sourceWidth = source.width;
        int sourceHeight = source.height;
        float sourceAspect = (float)sourceWidth / sourceHeight;
        float targetAspect = (float)targetWidth / targetHeight;
        int xOffset = 0;
        int yOffset = 0;
        float factor = 1;

        if (sourceAspect > targetAspect)
        { 
            factor = (float)targetHeight / sourceHeight;
            xOffset = (int)((sourceWidth - sourceHeight * targetAspect));
        }
        else
        { 
            factor = (float)targetWidth / sourceWidth;
            yOffset = (int)((sourceHeight - sourceWidth / targetAspect));
        }
        Color32[] data = source.GetPixels32();
        Color32[] data2 = new Color32[targetWidth * targetHeight];
        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                var p = new Vector2(Mathf.Clamp(xOffset + x / factor, 0, sourceWidth - 1), Mathf.Clamp(yOffset + y / factor, 0, sourceHeight - 1));
                // bilinear filtering
                var c11 = data[Mathf.FloorToInt(p.x) + sourceWidth * (Mathf.FloorToInt(p.y))];
                var c12 = data[Mathf.FloorToInt(p.x) + sourceWidth * (Mathf.CeilToInt(p.y))];
                var c21 = data[Mathf.CeilToInt(p.x) + sourceWidth * (Mathf.FloorToInt(p.y))];
                var c22 = data[Mathf.CeilToInt(p.x) + sourceWidth * (Mathf.CeilToInt(p.y))];
                var f = new Vector2(Mathf.Repeat(p.x, 1f), Mathf.Repeat(p.y, 1f));
                data2[x + y * targetWidth] = Color.Lerp(Color.Lerp(c11, c12, p.y), Color.Lerp(c21, c22, p.y), p.x);
            }
        }

        var tex = new Texture2D(targetWidth, targetHeight);
        tex.SetPixels32(data2);
        tex.Apply(true);
        return tex;
    }

    Tensor StandardizeTensor(Tensor img, double[] mean, double[] std)
    {
        Tensor img_copy = new Tensor(1, 256, 256, 3);
        for (int c = 0; c < 3; c++)
        {
            int channel = c;
            for (int i = 0; i < img.width; i++)
            {
                for (int j = 0; j < img.height; j++)
                {
                    img_copy[0, i, j, channel] = (img[0, i, j, channel] - (float)mean[c]) / (float)std[c];

                }
            }
        }


        return img_copy;
    }


    //transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225]) for IMAGENET
    Tensor ImageToTensor(Texture2D t)
    {
        var channelCount = 3;

        Texture2D crop = CropScale.CropTexture(t, new Vector2(175, 175), CropOptions.CENTER, 0, 0);

        //Custom Resize function
        Texture2D resized_img = Resize(crop, 256, 256);


        seeingImg.texture = resized_img;

        //Custom CenterCrop function
        //Texture2D center_crop_img = ResampleAndCrop(t, 256, 256);


        var img_tensor = new Tensor(resized_img, channelCount);

        //var n = CropPytorch(img_tensor, 256, 256);

        //Custom Standarization mean=[0.3418, 0.3126, 0.3224], std=[0.1627, 0.1632, 0.1731
        var std_tensor = StandardizeTensor(img_tensor, new double[] { 0.3418, 0.3126, 0.3224 }, new double[] { 0.1627, 0.1632, 0.1731 });

        byte[] bytes = resized_img.EncodeToJPG();

        if (Application.platform != RuntimePlatform.Android)
        {
            string filename = Application.dataPath + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".jpg";
            //File.WriteAllBytes(filename, bytes);
        }
        else
        {
            if (!PlayerPrefs.HasKey("PhotoSaved"))
            {
                PlayerPrefs.SetInt("PhotoSaved", 1);
            }
            else
            {
                PlayerPrefs.SetInt("PhotoSaved", PlayerPrefs.GetInt("PhotoSaved") + 1);
            }

            string filename = Application.persistentDataPath + PlayerPrefs.GetInt("PhotoSaved").ToString() + ".jpg";
            File.WriteAllBytes(filename, bytes);
        }


        img_tensor.Dispose();

        return std_tensor;
    }

    double[] FindBestK(double[] output, int k)
    {
        Array.Sort(output);
        Array.Reverse(output);
        double[] best = new double[k];

        for (int i = 0; i < k; i++)
        {
            best[i] = output[i];
        }

        return best;
    }

    double[] Softmax(double[] oSums)

    {

        double max = oSums[0];

        for (int i = 0; i < oSums.Length; ++i)

            if (oSums[i] > max) max = oSums[i];

        // determine scaling factor -- sum of exp(each val - max)

        double scale = 0.0;

        for (int i = 0; i < oSums.Length; ++i)

            scale += Math.Exp(oSums[i] - max);

        double[] result = new double[oSums.Length];

        for (int i = 0; i < oSums.Length; ++i)

            result[i] = Math.Exp(oSums[i] - max) / scale;

        return result;

    }


    void FreeMemory(Tensor outputs)
    {
        outputs.Dispose();
        worker.Dispose();
    }
    

    public void accuracyTest()
    {
        UnityEngine.Object[] images = Resources.LoadAll("accuracyTest", typeof(Texture2D));
        float accuracy_count = 0;
        float test_size = images.Length;

        foreach (Texture2D img in images)
        {
            string[] line_to_words = img.name.Split(' ');
            int index = int.Parse(line_to_words[line_to_words.Length - 1]);
            Tuple<double[], double[]> output = FeedModel(img);
            double[] best_idxs = output.Item1;

            foreach (int idx in best_idxs)
            {
                if (idx == index)
                {
                    accuracy_count += 1;
                }
            }
        }

        Debug.Log("accuracy: " + accuracy_count / test_size);
        accuracytext.text = "accuracy is: " + (System.Math.Round(accuracy_count / test_size, 2)).ToString();
    }


    public Tuple<double[], double[]> FeedModel(Texture2D t)
    {

        var img_t_ = ImageToTensor(t);

        worker.Execute(img_t_);

        var O =  worker.PeekOutput();

        img_t_.Dispose();

        //Store class probabilities 
        double[] outputs_probs = new double[O.length];
        
        for (int i = 0; i < O.length; i++)
        {
            outputs_probs[i] = O[0, 0, 0, i];
        }

        double[] sm_probs = Softmax(outputs_probs);

        Dictionary<double, int> pred_to_id = new Dictionary<double, int>();

        for(int i = 0; i < outputs_probs.Length; i++)
        {
            pred_to_id[sm_probs[i]] = i;
        }

        double[] best_probs = FindBestK(sm_probs, 3);
        double[] best_idxs = new double[3];
        for(int i = 0; i < 3; i++)
        {
            best_idxs[i] = pred_to_id[best_probs[i]];
        }

        return Tuple.Create(best_idxs, best_probs);
    }


}
